package handlers

import (
	"strings"
	"testing"
)

func TestFinderBundleRewriteEmitsProfileListAndAPILoaded(t *testing.T) {
	const path = "/t/wx_fed/finder/web/web-finder/res/js/virtual_svg-icons-register.a1b2.js"
	const source = `async finderUserPage(arg){return fetch(arg)}async next(){}export{finderUserPage}`
	got, handled := (&ScriptHandler{}).handleVirtualSvgIcons(path, source)
	if !handled || !strings.Contains(got, "WXU.Events.UserFeedsLoaded") || !strings.Contains(got, "WXU.Events.APILoaded") {
		t.Fatalf("finder bundle did not expose profile list events: handled=%v body=%s", handled, got)
	}
}
