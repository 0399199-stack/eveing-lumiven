package desktop

import (
	"errors"
	"os"
	"path/filepath"
	"testing"
	"wx_channel/internal/database"
)

func taskStore(t *testing.T) (*Tasks, string) {
	t.Helper()
	dir := t.TempDir()
	if err := database.Initialize(&database.Config{DBPath: filepath.Join(dir, "records.db")}); err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() { database.Close() })
	store, err := NewTasks(database.GetDB(), dir)
	if err != nil {
		t.Fatal(err)
	}
	return store, dir
}

func TestTaskCompletionAndRestart(t *testing.T) {
	s, dir := taskStore(t)
	id, err := s.Start("same-video", "测试视频", "EVEING", "1080p", "", 12)
	if err != nil {
		t.Fatal(err)
	}
	path := filepath.Join(dir, "video.mp4")
	if err = os.WriteFile(path, []byte("video-bytes"), 0600); err != nil {
		t.Fatal(err)
	}
	if err = s.Update(id, "processing", 10, 10); err != nil {
		t.Fatal(err)
	}
	if err = s.Complete(id, path, 10); err != nil {
		t.Fatal(err)
	}
	if err = s.Update(id, "downloading", 1, 10); err != nil {
		t.Fatal(err)
	}
	s.Fail(id, errors.New("late network failure"))
	record, err := database.NewDownloadRecordRepository().GetByID(id)
	if err != nil || record == nil || record.FilePath != path {
		t.Fatalf("record not committed: %v %v", record, err)
	}
	second, err := s.Start("same-video", "测试视频", "EVEING", "原始视频", "", 0)
	if err != nil {
		t.Fatal(err)
	}
	if second == id {
		t.Fatal("quality variants must have independent ids")
	}
	reloaded, err := NewTasks(database.GetDB(), dir)
	if err != nil {
		t.Fatal(err)
	}
	for _, task := range reloaded.Snapshot() {
		if task.ID == id && task.Status != "completed" {
			t.Fatalf("completed changed: %+v", task)
		}
		if task.ID == second && task.Status != "failed" {
			t.Fatalf("interrupted not failed: %+v", task)
		}
	}
}

func TestCompletionDoesNotLieOnInvalidFileOrDatabaseFailure(t *testing.T) {
	s, dir := taskStore(t)
	id, _ := s.Start("id", "video", "author", "original", "", 0)
	if err := s.Complete(id, filepath.Join(dir, "missing.mp4"), 0); err == nil {
		t.Fatal("missing file succeeded")
	}
	path := filepath.Join(dir, "video.mp4")
	os.WriteFile(path, []byte("video"), 0600)
	if _, err := database.GetDB().Exec(`DROP TABLE download_records`); err != nil {
		t.Fatal(err)
	}
	if err := s.Complete(id, path, 5); err == nil {
		t.Fatal("database failure succeeded")
	}
	if s.Snapshot()[0].Status == "completed" {
		t.Fatal("false completion")
	}
}
