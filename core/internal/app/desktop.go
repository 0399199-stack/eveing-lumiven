package app

import (
	"context"
	"crypto/subtle"
	"encoding/json"
	"fmt"
	"io"
	"net"
	"net/http"
	"os"
	"os/signal"
	"strings"
	"sync"
	"syscall"
	"time"

	"github.com/qtgolang/SunnyNet/SunnyNet"
	"github.com/qtgolang/SunnyNet/public"
	"github.com/qtgolang/SunnyNet/src/Certificate"
	"wx_channel/internal/database"
	"wx_channel/internal/desktop"
	"wx_channel/internal/utils"
)

type desktopStatus struct {
	State                 string `json:"state"`
	Error                 string `json:"error"`
	CertificateThumbprint string `json:"certificateThumbprint"`
	CertificateOwned      bool   `json:"certificateOwned"`
	Mode                  string `json:"mode"`
}

func (app *App) runDesktop() {
	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()
	var statusMu sync.Mutex
	status := desktopStatus{State: "starting", Mode: "system_proxy"}
	setFailed := func(err error) {
		statusMu.Lock()
		status.State = "failed"
		status.Error = err.Error()
		statusMu.Unlock()
		utils.Warn("Lumiven 接入失败: %v", err)
	}
	var lease *desktop.SystemLease
	defer func() {
		if app.UploadHandler != nil {
			app.UploadHandler.StopDownloads()
		}
		if app.BatchHandler != nil {
			app.BatchHandler.StopDownloads()
		}
		app.Sunny.Close()
		if !app.Cfg.DesktopTestMode {
			if err := desktop.RecoverSystem(app.Cfg.DesktopDataDir); err != nil {
				utils.Warn("恢复系统设置失败: %v", err)
			}
		}
		if app.GopeedService != nil && app.GopeedService.Downloader != nil {
			_ = app.GopeedService.Downloader.Close()
		}
		database.Close()
	}()
	mux := http.NewServeMux()
	mux.HandleFunc("/api/desktop/status", func(w http.ResponseWriter, r *http.Request) {
		if r.Method != "GET" {
			http.Error(w, "method not allowed", 405)
			return
		}
		statusMu.Lock()
		snapshot := status
		statusMu.Unlock()
		w.Header().Set("Content-Type", "application/json")
		json.NewEncoder(w).Encode(snapshot)
	})
	mux.HandleFunc("/api/desktop/shutdown", func(w http.ResponseWriter, r *http.Request) {
		if r.Method != "POST" {
			http.Error(w, "method not allowed", 405)
			return
		}
		statusMu.Lock()
		status.State = "stopping"
		statusMu.Unlock()
		w.Header().Set("Content-Type", "application/json")
		w.Write([]byte(`{"success":true}`))
		go cancel()
	})
	mux.HandleFunc("/api/desktop/tasks", func(w http.ResponseWriter, r *http.Request) {
		if app.DesktopTasks == nil {
			http.Error(w, "task database unavailable", 503)
			return
		}
		app.DesktopTasks.ServeTasks(w, r)
	})
	mux.HandleFunc("/ws/desktop", func(w http.ResponseWriter, r *http.Request) {
		if app.DesktopTasks == nil {
			http.Error(w, "task database unavailable", 503)
			return
		}
		app.DesktopTasks.ServeWebSocket(w, r)
	})
	auth := http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		expected := app.Cfg.DesktopToken
		if app.Cfg.DesktopTestMode && strings.HasPrefix(r.URL.Path, "/__wx_channels_api/") {
			expected = app.Cfg.SecretToken
		}
		if subtle.ConstantTimeCompare([]byte(r.Header.Get("X-Local-Auth")), []byte(expected)) != 1 {
			http.Error(w, "unauthorized", 401)
			return
		}
		mux.ServeHTTP(w, r)
	})
	listener, err := net.Listen("tcp", fmt.Sprintf("127.0.0.1:%d", app.Port+1))
	if err != nil {
		utils.Warn("管理端口不可用: %v", err)
		return
	}
	server := &http.Server{Handler: auth, ReadHeaderTimeout: 10 * time.Second}
	go func() {
		if err := server.Serve(listener); err != nil && err != http.ErrServerClosed {
			setFailed(err)
			cancel()
		}
	}()
	defer func() {
		shutdownCtx, stop := context.WithTimeout(context.Background(), 3*time.Second)
		defer stop()
		server.Shutdown(shutdownCtx)
	}()
	signals := make(chan os.Signal, 1)
	signal.Notify(signals, syscall.SIGINT, syscall.SIGTERM)
	defer signal.Stop(signals)
	go func() {
		select {
		case <-signals:
			cancel()
		case <-ctx.Done():
		}
	}()
	go func() { _ = desktop.WaitProcess(app.Cfg.DesktopParentPID); cancel() }()
	if err = app.initDownloadRecords(); err != nil {
		setFailed(err)
		<-ctx.Done()
		return
	}
	dir, err := app.Cfg.GetResolvedDownloadsDir()
	if err != nil {
		setFailed(err)
		<-ctx.Done()
		return
	}
	app.DesktopTasks, err = desktop.NewTasks(database.GetDB(), dir)
	if err != nil {
		setFailed(err)
		<-ctx.Done()
		return
	}
	app.initializeHandlers()
	app.UploadHandler.DesktopTasks = app.DesktopTasks
	app.BatchHandler.DesktopTasks = app.DesktopTasks
	go app.WSHub.Run()
	// Managed mode exposes only read-only legacy download records here.
	mux.Handle("/api/", app.APIRouter)
	if app.Cfg.DesktopTestMode {
		mux.HandleFunc("/__wx_channels_api/", func(w http.ResponseWriter, r *http.Request) {
			r.URL.Host = "channels.weixin.qq.com"
			r.URL.Scheme = "https"
			conn := &SunnyNet.HttpConn{Request: r, Type: public.HttpSendRequest}
			if !app.UploadHandler.Handle(conn) {
				app.BatchHandler.Handle(conn)
			}
			if conn.Response == nil {
				http.Error(w, "fixture route unavailable", 404)
				return
			}
			for name, values := range conn.Response.Header {
				for _, value := range values {
					w.Header().Add(name, value)
				}
			}
			w.WriteHeader(conn.Response.StatusCode)
			io.Copy(w, conn.Response.Body)
			conn.Response.Body.Close()
		})
		statusMu.Lock()
		status.State = "ready"
		status.Mode = "test_fixture"
		statusMu.Unlock()
		<-ctx.Done()
		return
	}
	if err = desktop.StartGuardian(app.Cfg.DesktopDataDir); err != nil {
		setFailed(err)
		<-ctx.Done()
		return
	}
	var certPEM, keyPEM []byte
	lease, certPEM, keyPEM, err = desktop.PrepareSystem(app.Cfg.DesktopDataDir, app.Port)
	if err != nil {
		setFailed(err)
		<-ctx.Done()
		return
	}
	statusMu.Lock()
	status.CertificateThumbprint = lease.Thumbprint
	status.CertificateOwned = lease.CertificateOwned
	statusMu.Unlock()
	managerID := Certificate.CreateCertificate()
	manager := Certificate.LoadCertificateContext(managerID)
	if manager == nil || !manager.LoadX509Certificate("", string(certPEM), string(keyPEM)) {
		setFailed(fmt.Errorf("本地代理证书加载失败"))
		<-ctx.Done()
		return
	}
	app.Sunny.SetCert(managerID)
	app.Sunny.SetPort(app.Port)
	app.Sunny.SetGoCallback(GlobalHttpCallback, nil, nil, nil)
	if err = app.Sunny.Start().Error; err != nil {
		setFailed(err)
		<-ctx.Done()
		return
	}
	if err = desktop.EnableProxy(lease); err != nil {
		setFailed(err)
		<-ctx.Done()
		return
	}
	statusMu.Lock()
	status.State = "ready"
	statusMu.Unlock()
	utils.Info("Lumiven 本地系统代理已启用；停止后恢复原设置")
	<-ctx.Done()
}
