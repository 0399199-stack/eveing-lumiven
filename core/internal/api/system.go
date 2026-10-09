package api

import (
	"net/http"
	"runtime"
	"time"
	"wx_channel/internal/response"
)

var startTime = time.Now()

// SystemService 系统服务
type SystemService struct{}

// NewSystemService 创建系统服务
func NewSystemService() *SystemService {
	return &SystemService{}
}

// SystemInfo 系统信息结构
type SystemInfo struct {
	OS            string `json:"os"`
	Arch          string `json:"arch"`
	NumCPU        int    `json:"num_cpu"`
	GoVersion     string `json:"go_version"`
	Goroutines    int    `json:"goroutines"`
	Uptime        string `json:"uptime"`
	UptimeSeconds int64  `json:"uptime_seconds"`
	Memory        struct {
		Alloc      uint64 `json:"alloc"`
		TotalAlloc uint64 `json:"total_alloc"`
		Sys        uint64 `json:"sys"`
		NumGC      uint32 `json:"num_gc"`
	} `json:"memory"`
}

// GetInfo 获取系统信息
func (s *SystemService) GetInfo(w http.ResponseWriter, r *http.Request) {
	var m runtime.MemStats
	runtime.ReadMemStats(&m)

	info := SystemInfo{
		OS:            runtime.GOOS,
		Arch:          runtime.GOARCH,
		NumCPU:        runtime.NumCPU(),
		GoVersion:     runtime.Version(),
		Goroutines:    runtime.NumGoroutine(),
		Uptime:        time.Since(startTime).String(),
		UptimeSeconds: int64(time.Since(startTime).Seconds()),
	}

	info.Memory.Alloc = m.Alloc
	info.Memory.TotalAlloc = m.TotalAlloc
	info.Memory.Sys = m.Sys
	info.Memory.NumGC = m.NumGC

	response.Success(w, info)
}

// GetHealth 健康检查
func (s *SystemService) GetHealth(w http.ResponseWriter, r *http.Request) {
	response.Success(w, map[string]string{
		"status": "ok",
		"time":   time.Now().Format(time.RFC3339),
	})
}

// RegisterRoutes 注册路由
func (s *SystemService) RegisterRoutes(mux *http.ServeMux) {
	mux.HandleFunc("/api/v1/system/info", s.GetInfo)
	mux.HandleFunc("/api/v1/system/health", s.GetHealth)
}
