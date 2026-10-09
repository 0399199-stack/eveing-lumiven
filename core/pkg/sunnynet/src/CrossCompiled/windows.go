//go:build windows
// +build windows

package CrossCompiled

import "C"
import (
	"bufio"
	"bytes"
	"github.com/Trisia/gosysproxy"
	"github.com/qtgolang/SunnyNet/public"
	"github.com/qtgolang/SunnyNet/src/iphlpapi"

	"io"
	"os"
	"os/exec"
	"strconv"
	"syscall"
)

// Process capture is unavailable in the redistributable proxy build.
func NFapi_SunnyPointer(a ...uintptr) uintptr                  { return 0 }
func NFapi_IsInit(a ...bool) bool                              { return false }
func NFapi_ProcessPortInt(a ...uint16) uint16                  { return 0 }
func NFapi_ApiInit() bool                                      { return false }
func NFapi_MessageBox(caption, text string, style uintptr) int { return 0 }
func NFapi_SetHookProcess(open bool)                           {}
func NFapi_ClosePidTCP(pid int)                                {}
func NFapi_DelName(u string)                                   {}
func NFapi_AddName(u string)                                   {}
func NFapi_DelPid(pid uint32)                                  {}
func NFapi_AddPid(pid uint32)                                  {}
func NFapi_CloseNameTCP(u string)                              {}
func NFapi_CancelAll()                                         {}
func NFapi_DelTcpConnectInfo(u uint16)                         {}
func NFapi_GetTcpConnectInfo(u uint16) *ProcessInfo            { return nil }
func NFapi_API_NfTcpClose(u uint64)                            {}
func NFapi_UdpSendReceiveFunc(udp func(int8, int64, uint32, string, string, []byte) []byte) func(int8, int64, uint32, string, string, []byte) []byte {
	return nil
}
func SetIeProxy(Off bool, Port int) bool {
	// "github.com/Tri sia/gos ysp roxy"
	if Off {
		_ = gosysproxy.Off()
		return true
	}
	ies := "127.0.0.1:" + strconv.Itoa(Port)
	_ = gosysproxy.SetGlobalProxy("http="+ies+";https="+ies, "")
	return true
}

// InstallCert 安装证书 将证书安装到Windows系统内
func InstallCert(certificates []byte) string {
	path, err := os.Getwd()
	if err != nil {
		return err.Error()
	}
	err = public.WriteBytesToFile(certificates, path+"\\ca.crt")
	if err != nil {
		return err.Error()
	}
	var args []string
	args = append(args, "-addstore")
	args = append(args, "root")
	args = append(args, path+"\\ca.crt")
	defer func() { _ = public.RemoveFile(path + "\\ca.crt") }()
	cmd := exec.Command("certutil", args...)
	stdout, err := cmd.StdoutPipe()
	if err != nil {
		return err.Error()
	}
	cmd.SysProcAttr = &syscall.SysProcAttr{HideWindow: true}
	_ = cmd.Start()
	var Buff bytes.Buffer
	reader := bufio.NewReader(stdout)
	for {
		line, err2 := reader.ReadBytes('\n')
		if err2 != nil || io.EOF == err2 {
			break
		}
		Buff.Write(line)
	}
	return Buff.String()
}

func SetNetworkConnectNumber() {}

// CloseCurrentSocket  关闭指定进程的所有TCP连接
func CloseCurrentSocket(PID int, ulAf uint) {
	iphlpapi.CloseCurrentSocket(PID, ulAf)
}

// GetTcpInfoPID 用于获取指定 TCP 连接信息的 PID
func GetTcpInfoPID(tcpInfo string, SunnyPort int) string {
	return iphlpapi.GetTcpInfoPID(tcpInfo, SunnyPort)
}
