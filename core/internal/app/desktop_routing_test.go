package app

import (
	"net/http"
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

func TestManagedProxyInterceptsOnlyChannelsAndFinderBundle(t *testing.T) {
	const finderBundle = "/t/wx_fed/finder/web/web-finder/res/js/virtual_svg-icons-register.a1b2.js"
	for _, target := range []struct {
		url  string
		want bool
	}{
		{"https://example.com/console", false},
		{"https://example.com/api/downloads", false},
		{"https://channels.weixin.qq.com/web/pages/home", true},
		{"https://CHANNELS.WEIXIN.QQ.COM/web/pages/home", true},
		{"https://res.wx.qq.com" + finderBundle, true},
		{"https://RES.WX.QQ.COM" + finderBundle, true},
		{"https://res.wx.qq.com/t/wx_fed/finder/web/web-finder/res/js/other.js", false},
		{"https://res.wx.qq.com/web/pages/profile", false},
		{"https://res.wx.qq.com.evil.test" + finderBundle, false},
	} {
		for _, kind := range []int{public.HttpSendRequest, public.HttpResponseOK} {
			probe := &routeProbe{}
			app := &App{Cfg: &config.Config{DesktopManaged: true}, requestInterceptors: []router.Interceptor{probe}, responseInterceptors: []router.Interceptor{probe}}
			request := httptest.NewRequest("GET", target.url, nil)
			request.Header.Set("Accept-Encoding", "gzip")
			app.HandleRequest(&SunnyNet.HttpConn{Request: request, Type: kind})
			if probe.called != target.want {
				t.Fatalf("%s type=%d: interception = %v, want %v", target.url, kind, probe.called, target.want)
			}
			if kind == public.HttpSendRequest && target.want && request.Header.Get("Accept-Encoding") != "" {
				t.Fatalf("%s: intercepted request encoding was not removed", target.url)
			}
			if (!target.want || kind == public.HttpResponseOK) && request.Header.Get("Accept-Encoding") != "gzip" {
				t.Fatalf("%s: unrelated request headers changed", target.url)
			}
		}
	}
}

func TestManagedInterceptTargetRejectsMalformedRequest(t *testing.T) {
	for _, conn := range []*SunnyNet.HttpConn{
		nil,
		{},
		{Request: &http.Request{}},
	} {
		if isManagedInterceptTarget(conn) {
			t.Fatal("malformed request was accepted")
		}
	}
}
