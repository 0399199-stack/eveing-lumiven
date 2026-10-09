package app

import (
	"net/http/httptest"
	"testing"

	"github.com/qtgolang/SunnyNet/SunnyNet"
	"github.com/qtgolang/SunnyNet/public"
	"wx_channel/internal/config"
	"wx_channel/internal/router"
)

type routeProbe struct{ called bool }

func (probe *routeProbe) Handle(*SunnyNet.HttpConn) bool {
	probe.called = true
	return true
}

func TestManagedProxyOnlyInterceptsChannelsHost(t *testing.T) {
	for _, target := range []struct {
		url  string
		want bool
	}{
		{"https://example.com/console", false},
		{"https://example.com/api/downloads", false},
		{"https://channels.weixin.qq.com/web/pages/home", true},
		{"https://CHANNELS.WEIXIN.QQ.COM/web/pages/home", true},
	} {
		probe := &routeProbe{}
		app := &App{Cfg: &config.Config{DesktopManaged: true}, requestInterceptors: []router.Interceptor{probe}}
		request := httptest.NewRequest("GET", target.url, nil)
		request.Header.Set("Accept-Encoding", "gzip")
		app.HandleRequest(&SunnyNet.HttpConn{Request: request, Type: public.HttpSendRequest})
		if probe.called != target.want {
			t.Fatalf("%s: interception = %v, want %v", target.url, probe.called, target.want)
		}
		if !target.want && request.Header.Get("Accept-Encoding") != "gzip" {
			t.Fatalf("%s: unrelated request headers changed", target.url)
		}
	}
}
