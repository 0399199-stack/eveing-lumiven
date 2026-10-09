package handlers

import (
	"io"
	"net/http"
	"strings"
	"testing"

	"wx_channel/internal/config"

	"github.com/qtgolang/SunnyNet/SunnyNet"
)

func TestManagedScriptInjectionKeepsNativeModuleURLs(t *testing.T) {
	cfg := config.Get()
	oldManaged, oldSavePageJS := cfg.DesktopManaged, cfg.SavePageJS
	cfg.DesktopManaged, cfg.SavePageJS = true, false
	t.Cleanup(func() {
		cfg.DesktopManaged, cfg.SavePageJS = oldManaged, oldSavePageJS
	})

	h := &ScriptHandler{version: "?t=lumiven-test"}
	const workerURL = "./worker-client.publishA.js"
	const jsPath = "/t/wx_fed/finder/web/web-finder/res/js/virtual_svg-icons-register.publishA.js"
	js := `import{worker}from"` + workerURL + `";const deps=["js/worker-client.publishA.js"];const load=()=>import("` + workerURL + `");class API{async finderUserPage(arg){return this.post(arg)}async next(){}}const api=new API();export{api}`
	jsConn := &SunnyNet.HttpConn{Response: &http.Response{Header: http.Header{"Content-Type": []string{"application/javascript"}}}}
	if !h.HandleJavaScriptResponse(jsConn, "res.wx.qq.com", jsPath, []byte(js)) {
		t.Fatal("finder bundle was not handled")
	}
	jsBody, err := io.ReadAll(jsConn.Response.Body)
	if err != nil {
		t.Fatal(err)
	}
	if strings.Count(string(jsBody), workerURL) != 2 || !strings.Contains(string(jsBody), `"js/worker-client.publishA.js"`) {
		t.Fatalf("managed finder bundle changed native worker URLs: %s", jsBody)
	}
	if !strings.Contains(string(jsBody), "WXU.Events.APILoaded") {
		t.Fatalf("managed finder bundle lost API injection: %s", jsBody)
	}

	html := `<html><head><script src="` + workerURL + `"></script><link href="` + workerURL + `"></head></html>`
	htmlConn := &SunnyNet.HttpConn{Response: &http.Response{Header: http.Header{"Content-Type": []string{"text/html"}}}}
	if !h.HandleHTMLResponse(htmlConn, "channels.weixin.qq.com", "/web/pages/unknown", []byte(html)) {
		t.Fatal("HTML page was not handled")
	}
	htmlBody, err := io.ReadAll(htmlConn.Response.Body)
	if err != nil {
		t.Fatal(err)
	}
	if string(htmlBody) != html {
		t.Fatalf("managed page changed native script URLs: %s", htmlBody)
	}

	cfg.DesktopManaged = false
	legacyConn := &SunnyNet.HttpConn{Response: &http.Response{Header: http.Header{"Content-Type": []string{"application/javascript"}}}}
	h.HandleJavaScriptResponse(legacyConn, "res.wx.qq.com", jsPath, []byte(js))
	legacyBody, err := io.ReadAll(legacyConn.Response.Body)
	if err != nil {
		t.Fatal(err)
	}
	if !strings.Contains(string(legacyBody), workerURL+"?t=lumiven-test") {
		t.Fatalf("non-managed version behavior changed: %s", legacyBody)
	}
}
