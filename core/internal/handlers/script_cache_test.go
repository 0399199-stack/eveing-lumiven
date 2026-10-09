package handlers

import (
	"io"
	"net/http"
	"net/http/httptest"
	"reflect"
	"strings"
	"testing"

	"github.com/qtgolang/SunnyNet/SunnyNet"
	"github.com/qtgolang/SunnyNet/public"
)

func cachedScriptHeaders(contentType string) http.Header {
	return http.Header{
		"Content-Type":  {contentType},
		"Cache-Control": {"max-age=31536000"},
		"Etag":          {"upstream-etag"},
		"Last-Modified": {"Thu, 08 Oct 2026 15:11:26 GMT"},
		"Accept-Ranges": {"bytes"},
		"Content-Range": {"bytes 0-100/101"},
	}
}

func assertModifiedScriptHeaders(t *testing.T, header http.Header) {
	t.Helper()
	if got := header.Get("Cache-Control"); got != "no-store" {
		t.Fatalf("modified response cache control = %q", got)
	}
	for _, name := range []string{"ETag", "Last-Modified", "Accept-Ranges", "Content-Range"} {
		if value := header.Get(name); value != "" {
			t.Fatalf("modified response kept %s=%q", name, value)
		}
	}
}

func TestScriptHandlerDisablesCachingOnlyForModifiedBodies(t *testing.T) {
	h := &ScriptHandler{}
	html := `<html><head></head><body>test</body></html>`
	htmlConn := &SunnyNet.HttpConn{Response: &http.Response{StatusCode: http.StatusOK, Header: cachedScriptHeaders("text/html")}}
	if !h.HandleHTMLResponse(htmlConn, "channels.weixin.qq.com", "/web/pages/feed", []byte(html)) {
		t.Fatal("feed HTML was not handled")
	}
	assertModifiedScriptHeaders(t, htmlConn.Response.Header)
	modifiedHTML, _ := io.ReadAll(htmlConn.Response.Body)
	if string(modifiedHTML) == html {
		t.Fatal("feed HTML was not injected")
	}

	js := `class API{async finderUserPage(t){return this.post(t)}async next(){}}export{API}`
	jsConn := &SunnyNet.HttpConn{Response: &http.Response{StatusCode: http.StatusOK, Header: cachedScriptHeaders("application/javascript")}}
	if !h.HandleJavaScriptResponse(jsConn, "res.wx.qq.com", "/t/wx_fed/finder/web/web-finder/res/js/virtual_svg-icons-register.test.js", []byte(js)) {
		t.Fatal("finder bundle was not handled")
	}
	assertModifiedScriptHeaders(t, jsConn.Response.Header)
	modifiedJS, _ := io.ReadAll(jsConn.Response.Body)
	if !strings.Contains(string(modifiedJS), "WXU.Events.APILoaded") {
		t.Fatal("finder bundle was not injected")
	}

	for _, tc := range []struct {
		name, contentType, path, body string
	}{
		{"unmodified HTML", "text/html", "/web/pages/unknown", html},
		{"unmodified JS", "application/javascript", "/other.js", `console.log(1)`},
		{"unmodified connect JS", "application/javascript", "/connect.publish.js", `console.log(1)`},
	} {
		t.Run(tc.name, func(t *testing.T) {
			header := cachedScriptHeaders(tc.contentType)
			originalHeader := header.Clone()
			conn := &SunnyNet.HttpConn{Response: &http.Response{StatusCode: http.StatusOK, Header: header}}
			if tc.contentType == "text/html" {
				h.HandleHTMLResponse(conn, "channels.weixin.qq.com", tc.path, []byte(tc.body))
			} else {
				h.HandleJavaScriptResponse(conn, "channels.weixin.qq.com", tc.path, []byte(tc.body))
			}
			gotBody, _ := io.ReadAll(conn.Response.Body)
			if string(gotBody) != tc.body || !reflect.DeepEqual(conn.Response.Header, originalHeader) {
				t.Fatalf("unchanged response was modified: body=%q headers=%v", gotBody, conn.Response.Header)
			}
		})
	}
}

func TestScriptHandlerLeavesPartialAndNotModifiedResponsesUntouched(t *testing.T) {
	for _, status := range []int{http.StatusPartialContent, http.StatusNotModified} {
		header := cachedScriptHeaders("text/html")
		originalHeader := header.Clone()
		const body = `<html><head></head><body>test</body></html>`
		conn := &SunnyNet.HttpConn{
			Type:     public.HttpResponseOK,
			Request:  httptest.NewRequest(http.MethodGet, "https://channels.weixin.qq.com/web/pages/feed", nil),
			Response: &http.Response{StatusCode: status, Header: header, Body: io.NopCloser(strings.NewReader(body))},
		}
		if (&ScriptHandler{}).Handle(conn) {
			t.Fatalf("status %d was intercepted", status)
		}
		gotBody, _ := io.ReadAll(conn.Response.Body)
		if string(gotBody) != body || !reflect.DeepEqual(conn.Response.Header, originalHeader) {
			t.Fatalf("status %d changed: body=%q headers=%v", status, gotBody, conn.Response.Header)
		}
	}
}
