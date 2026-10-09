package handlers

import (
	"strings"
	"testing"

	"github.com/dop251/goja"
)

func TestFinderBundleRewriteEmitsProfileListAndAPILoaded(t *testing.T) {
	const path = "/t/wx_fed/finder/web/web-finder/res/js/virtual_svg-icons-register.a1b2.js"
	const source = `async finderUserPage(arg){return fetch(arg)}async next(){}export{finderUserPage}`
	got, handled := (&ScriptHandler{}).handleVirtualSvgIcons(path, source)
	if !handled || !strings.Contains(got, "WXU.Events.UserFeedsLoaded") || !strings.Contains(got, "WXU.Events.APILoaded") {
		t.Fatalf("finder bundle did not expose profile list events: handled=%v body=%s", handled, got)
	}
}

func TestFinderBundleObserversCannotBreakNativeModule(t *testing.T) {
	const path = "/t/wx_fed/finder/web/web-finder/res/js/virtual_svg-icons-register.a1b2.js"
	const exports = `export{finderGetCommentDetail,finderGetCommentList,finderUserPage,finderLiveUserPage}`
	const source = `class API{async finderGetCommentDetail(arg){return Promise.resolve({data:{object:arg}})}async finderGetCommentList(arg){return Promise.resolve({data:[arg]})}async finderUserPage(arg){return arg<0?Promise.reject(new Error("native failure")):Promise.resolve({data:{object:[arg]}})}async finderLiveUserPage(arg){return Promise.resolve({data:{object:[arg]}})}async next(){}}const api=new API();const finderGetCommentDetail=api.finderGetCommentDetail.bind(api),finderGetCommentList=api.finderGetCommentList.bind(api),finderUserPage=api.finderUserPage.bind(api),finderLiveUserPage=api.finderLiveUserPage.bind(api);` + exports
	got, handled := (&ScriptHandler{}).handleVirtualSvgIcons(path, source)
	if !handled || !strings.HasSuffix(got, exports) {
		t.Fatalf("finder bundle rewrite changed native exports: handled=%v body=%s", handled, got)
	}

	for _, tc := range []struct {
		name  string
		setup string
	}{
		{"missing WXU", `var console={log(){},warn(){}};`},
		{"throwing observer", `var console={log(){},warn(){}};var WXU={Events:{APILoaded:1,FeedProfileLoaded:2,FeedCommentListLoaded:3,UserFeedsLoaded:4,UserLiveReplayLoaded:5},emit(){throw new Error("observer failed")}};`},
	} {
		t.Run(tc.name, func(t *testing.T) {
			vm := goja.New()
			if _, err := vm.RunString(tc.setup + strings.TrimSuffix(got, exports)); err != nil {
				t.Fatalf("injected module failed to load: %v", err)
			}
			for _, method := range []string{"finderGetCommentDetail", "finderGetCommentList", "finderUserPage", "finderLiveUserPage"} {
				value, err := vm.RunString("api." + method + "(7)")
				if err != nil {
					t.Fatalf("%s call failed: %v", method, err)
				}
				promise, ok := value.Export().(*goja.Promise)
				if !ok || promise.State() != goja.PromiseStateFulfilled {
					t.Fatalf("%s native result was lost: %v", method, value)
				}
				data := promise.Result().ToObject(vm).Get("data").ToObject(vm)
				var original goja.Value
				switch method {
				case "finderGetCommentDetail":
					original = data.Get("object")
				case "finderGetCommentList":
					original = data.Get("0")
				default:
					original = data.Get("object").ToObject(vm).Get("0")
				}
				if original.ToInteger() != 7 {
					t.Fatalf("%s changed native result: %v", method, promise.Result())
				}
			}
			value, err := vm.RunString("api.finderUserPage(-1)")
			if err != nil {
				t.Fatalf("native rejected call failed: %v", err)
			}
			promise := value.Export().(*goja.Promise)
			if promise.State() != goja.PromiseStateRejected || !strings.Contains(promise.Result().String(), "native failure") {
				t.Fatalf("native rejection changed: state=%v result=%v", promise.State(), promise.Result())
			}
		})
	}
}
