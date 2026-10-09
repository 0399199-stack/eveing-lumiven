package desktop

import (
	"context"
	"database/sql"
	"encoding/json"
	"errors"
	"net/http"
	"os"
	"path/filepath"
	"sort"
	"sync"
	"time"

	"github.com/coder/websocket"
	"github.com/google/uuid"
)

// Task is the desktop protocol. Paths are returned only to an authenticated local client.
type Task struct {
	ID              string    `json:"id"`
	VideoID         string    `json:"videoId"`
	Title           string    `json:"title"`
	Author          string    `json:"author"`
	Quality         string    `json:"quality"`
	CoverURL        string    `json:"coverUrl,omitempty"`
	Status          string    `json:"status"`
	DownloadedBytes int64     `json:"downloadedBytes"`
	TotalBytes      int64     `json:"totalBytes"`
	Speed           int64     `json:"speed"`
	FilePath        string    `json:"filePath"`
	RelativePath    string    `json:"relativePath"`
	Error           string    `json:"error"`
	CreatedAt       time.Time `json:"createdAt"`
	UpdatedAt       time.Time `json:"updatedAt"`
}

type Event struct {
	Type string `json:"type"`
	Task Task   `json:"task"`
}
type Tasks struct {
	mu      sync.Mutex
	db      *sql.DB
	dir     string
	items   map[string]Task
	clients map[chan Event]struct{}
}

func NewTasks(db *sql.DB, downloadDir string) (*Tasks, error) {
	s := &Tasks{db: db, dir: downloadDir, items: map[string]Task{}, clients: map[chan Event]struct{}{}}
	if _, err := db.Exec(`CREATE TABLE IF NOT EXISTS desktop_tasks (id TEXT PRIMARY KEY, payload TEXT NOT NULL)`); err != nil {
		return nil, err
	}
	rows, err := db.Query(`SELECT payload FROM desktop_tasks`)
	if err != nil {
		return nil, err
	}
	for rows.Next() {
		var payload string
		if err = rows.Scan(&payload); err != nil {
			rows.Close()
			return nil, err
		}
		var task Task
		if err = json.Unmarshal([]byte(payload), &task); err != nil {
			rows.Close()
			return nil, err
		}
		s.items[task.ID] = task
	}
	err = rows.Err()
	rows.Close()
	if err != nil {
		return nil, err
	}
	// An interrupted download is never advertised as completed after a restart.
	for id, task := range s.items {
		if task.Status != "completed" && task.Status != "failed" {
			task.Status = "failed"
			task.Error = "上次接入已中断，请在微信中重新下载"
			task.Speed = 0
			task.UpdatedAt = time.Now()
			s.items[id] = task
			if err = s.persist(task); err != nil {
				return nil, err
			}
		}
	}
	return s, nil
}

func (s *Tasks) persist(t Task) error {
	b, err := json.Marshal(t)
	if err != nil {
		return err
	}
	_, err = s.db.Exec(`INSERT OR REPLACE INTO desktop_tasks(id,payload) VALUES(?,?)`, t.ID, string(b))
	return err
}

func (s *Tasks) Start(videoID, title, author, quality, cover string, total int64) (string, error) {
	s.mu.Lock()
	defer s.mu.Unlock()
	now := time.Now()
	t := Task{ID: uuid.NewString(), VideoID: videoID, Title: title, Author: author, Quality: quality, CoverURL: cover, Status: "pending", TotalBytes: total, CreatedAt: now, UpdatedAt: now}
	if err := s.persist(t); err != nil {
		return "", err
	}
	s.items[t.ID] = t
	s.emit("task_updated", t)
	return t.ID, nil
}

func (s *Tasks) emit(kind string, t Task) {
	for ch := range s.clients {
		select {
		case ch <- Event{kind, t}:
		default:
		}
	}
}

func (s *Tasks) Update(id, status string, downloaded, total int64) error {
	if s == nil || id == "" {
		return nil
	}
	s.mu.Lock()
	defer s.mu.Unlock()
	t, ok := s.items[id]
	if !ok {
		return errors.New("task not found")
	}
	if t.Status == "completed" || t.Status == "failed" {
		return nil
	}
	now := time.Now()
	if status == t.Status && now.Sub(t.UpdatedAt) < 250*time.Millisecond {
		return nil
	}
	if dt := now.Sub(t.UpdatedAt).Seconds(); dt > 0 && downloaded >= t.DownloadedBytes {
		t.Speed = int64(float64(downloaded-t.DownloadedBytes) / dt)
	}
	t.Status = status
	t.DownloadedBytes = downloaded
	if total > 0 {
		t.TotalBytes = total
	}
	t.UpdatedAt = now
	if err := s.persist(t); err != nil {
		return err
	}
	s.items[id] = t
	s.emit("task_updated", t)
	return nil
}

func (s *Tasks) Fail(id string, err error) {
	if s == nil || id == "" || err == nil {
		return
	}
	s.mu.Lock()
	defer s.mu.Unlock()
	t, ok := s.items[id]
	if !ok || t.Status == "completed" || t.Status == "failed" {
		return
	}
	t.Status = "failed"
	t.Error = err.Error()
	t.Speed = 0
	t.UpdatedAt = time.Now()
	_ = s.persist(t)
	s.items[id] = t
	s.emit("task_failed", t)
}

// Complete commits the media-library record and task atomically, after the final file exists.
func (s *Tasks) Complete(id, path string, size int64) error {
	if s == nil || id == "" {
		return nil
	}
	stat, err := os.Stat(path)
	if err != nil {
		return err
	}
	if stat.Size() <= 0 {
		return errors.New("保存的视频为空")
	}
	s.mu.Lock()
	defer s.mu.Unlock()
	t, ok := s.items[id]
	if !ok {
		return errors.New("task not found")
	}
	t.Status = "completed"
	t.FilePath = path
	t.RelativePath, _ = filepath.Rel(s.dir, path)
	t.TotalBytes = stat.Size()
	t.DownloadedBytes = stat.Size()
	t.Speed = 0
	t.Error = ""
	t.UpdatedAt = time.Now()
	b, err := json.Marshal(t)
	if err != nil {
		return err
	}
	tx, err := s.db.Begin()
	if err != nil {
		return err
	}
	defer tx.Rollback()
	_, err = tx.Exec(`INSERT OR REPLACE INTO download_records (id,video_id,title,author,cover_url,duration,file_size,file_path,format,resolution,status,download_time,error_message,like_count,comment_count,forward_count,fav_count,created_at,updated_at) VALUES(?,?,?,?,?,0,?,?, 'mp4',?,'completed',?,'',0,0,0,0,?,?)`, t.ID, t.VideoID, t.Title, t.Author, t.CoverURL, stat.Size(), path, t.Quality, t.UpdatedAt, t.CreatedAt, t.UpdatedAt)
	if err != nil {
		return err
	}
	if _, err = tx.Exec(`INSERT OR REPLACE INTO desktop_tasks(id,payload) VALUES(?,?)`, id, string(b)); err != nil {
		return err
	}
	if err = tx.Commit(); err != nil {
		return err
	}
	s.items[id] = t
	s.emit("task_completed", t)
	return nil
}

func (s *Tasks) Snapshot() []Task {
	s.mu.Lock()
	defer s.mu.Unlock()
	list := make([]Task, 0, len(s.items))
	for _, t := range s.items {
		list = append(list, t)
	}
	sort.Slice(list, func(i, j int) bool { return list[i].CreatedAt.After(list[j].CreatedAt) })
	return list
}

func (s *Tasks) ServeTasks(w http.ResponseWriter, r *http.Request) {
	if r.Method != http.MethodGet {
		http.Error(w, "method not allowed", 405)
		return
	}
	w.Header().Set("Content-Type", "application/json")
	_ = json.NewEncoder(w).Encode(map[string]any{"tasks": s.Snapshot()})
}

func (s *Tasks) ServeWebSocket(w http.ResponseWriter, r *http.Request) {
	conn, err := websocket.Accept(w, r, &websocket.AcceptOptions{OriginPatterns: []string{"127.0.0.1:*", "localhost:*"}})
	if err != nil {
		return
	}
	defer conn.CloseNow()
	ctx, cancel := context.WithCancel(r.Context())
	defer cancel()
	ch := make(chan Event, 64)
	s.mu.Lock()
	s.clients[ch] = struct{}{}
	s.mu.Unlock()
	defer func() { s.mu.Lock(); delete(s.clients, ch); s.mu.Unlock() }()
	go func() {
		for {
			if _, _, err := conn.Read(ctx); err != nil {
				cancel()
				return
			}
		}
	}()
	for {
		select {
		case <-ctx.Done():
			return
		case event := <-ch:
			payload, _ := json.Marshal(event)
			writeCtx, writeCancel := context.WithTimeout(ctx, 5*time.Second)
			err := conn.Write(writeCtx, websocket.MessageText, payload)
			writeCancel()
			if err != nil {
				return
			}
		}
	}
}
