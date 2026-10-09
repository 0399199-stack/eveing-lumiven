package handlers

import (
	"context"
	"encoding/hex"
	"encoding/json"
	"fmt"
	"github.com/qtgolang/SunnyNet/SunnyNet"
	"io"
	"net/http"
	"wx_channel/internal/utils"
)

func validUploadID(id string) bool {
	if len(id) != 32 {
		return false
	}
	_, err := hex.DecodeString(id)
	return err == nil
}

func (h *UploadHandler) StopDownloads() {
	h.activeDownloads.Range(func(_, value any) bool {
		if cancel, ok := value.(context.CancelFunc); ok {
			cancel()
		}
		return true
	})
	h.downloadWG.Wait()
}
func (h *BatchHandler) StopDownloads() {
	h.mu.Lock()
	h.stopping = true
	cancel, done := h.cancelFunc, h.downloadDone
	h.mu.Unlock()
	if cancel != nil {
		cancel()
	}
	if done != nil {
		<-done
	}
}

func (h *UploadHandler) HandleDesktopPage(conn *SunnyNet.HttpConn) bool {
	path := conn.Request.URL.Path
	if path != "/__wx_channels_api/desktop_begin" && path != "/__wx_channels_api/desktop_progress" {
		return false
	}
	if h.DesktopTasks == nil {
		h.sendErrorResponse(conn, fmt.Errorf("desktop unavailable"))
		return true
	}
	if conn.Request.Method != http.MethodPost {
		h.sendErrorResponse(conn, fmt.Errorf("method not allowed"))
		return true
	}
	if cfg := h.getConfig(); cfg == nil || conn.Request.Header.Get("X-Local-Auth") != cfg.SecretToken {
		h.sendErrorResponse(conn, fmt.Errorf("unauthorized"))
		return true
	}
	if conn.Request.URL.Hostname() != "channels.weixin.qq.com" {
		h.sendErrorResponse(conn, fmt.Errorf("invalid host"))
		return true
	}
	var request struct {
		ID         string `json:"id"`
		VideoID    string `json:"videoId"`
		Title      string `json:"title"`
		Author     string `json:"author"`
		Quality    string `json:"quality"`
		CoverURL   string `json:"coverUrl"`
		Status     string `json:"status"`
		Downloaded int64  `json:"downloadedBytes"`
		Total      int64  `json:"totalBytes"`
		Error      string `json:"error"`
	}
	if err := json.NewDecoder(io.LimitReader(conn.Request.Body, 65536)).Decode(&request); err != nil {
		h.sendErrorResponse(conn, err)
		return true
	}
	if path == "/__wx_channels_api/desktop_begin" {
		id, err := h.DesktopTasks.Start(request.VideoID, request.Title, request.Author, request.Quality, request.CoverURL, request.Total)
		if err != nil {
			h.sendErrorResponse(conn, err)
			return true
		}
		h.sendJSONResponse(conn, 200, map[string]any{"success": true, "id": id})
		return true
	}
	if request.Error != "" {
		h.DesktopTasks.Fail(request.ID, fmt.Errorf("%s", request.Error))
	} else {
		if request.Status != "downloading" && request.Status != "saving" && request.Status != "processing" {
			h.sendErrorResponse(conn, fmt.Errorf("invalid task phase"))
			return true
		}
		if err := h.DesktopTasks.Update(request.ID, request.Status, request.Downloaded, request.Total); err != nil {
			utils.Warn("记录页面下载进度: %v", err)
			h.sendErrorResponse(conn, err)
			return true
		}
	}
	h.sendJSONResponse(conn, 200, map[string]any{"success": true})
	return true
}
