package api

import (
	"net/http"

	"wx_channel/internal/response"

	"github.com/qtgolang/SunnyNet/SunnyNet"
)

// ProxyService 代理服务
type ProxyService struct {
	sunny *SunnyNet.Sunny
	port  int // 当前监听端口
}

// NewProxyService 创建代理服务
func NewProxyService(sunny *SunnyNet.Sunny, port int) *ProxyService {
	return &ProxyService{
		sunny: sunny,
		port:  port,
	}
}

// GetStatus 获取代理状态
func (s *ProxyService) GetStatus(w http.ResponseWriter, r *http.Request) {
	if s.sunny == nil {
		response.Error(w, 500, "Proxy service not initialized")
		return
	}

	// SunnyNet 没有直接的 "IsRunning" 方法，但我们可以返回配置的端口和任何可知状态
	// 实际应用中可能需要维护一个 Running 状态变量
	status := map[string]interface{}{
		"running": true, // 假设只要服务在跑就是 running
		"port":    s.port,
		"version": "SunnyNet (latest)", // 无法直接获取版本？
		"mode":    "中间人代理 (MITM)",
	}
	response.Success(w, status)
}

// Restart 重启代理 (模拟)
// 真正的重启可能涉及停止 SunnyNet 并重新绑定端口
func (s *ProxyService) Restart(w http.ResponseWriter, r *http.Request) {
	if s.sunny == nil {
		response.Error(w, 500, "Proxy service not initialized")
		return
	}

	// 实际代码需要调用 sunny.SetPort 等
	// 鉴于 SunnyNet API 的复杂性，这里暂时只返回“重启指令已接收”
	// TODO: 实现真正的重启逻辑

	// SunnyNet 的 SetPort 和 Start 是链式调用的
	err := s.sunny.SetPort(s.port).Start().Error
	if err != nil {
		response.Error(w, 500, err.Error())
		return
	}

	response.Success(w, "Proxy restarted")
}

// RegisterRoutes 注册路由
func (s *ProxyService) RegisterRoutes(mux *http.ServeMux) {
	mux.HandleFunc("/api/v1/proxy/status", s.GetStatus)
	mux.HandleFunc("/api/v1/proxy/restart", s.Restart)
}
