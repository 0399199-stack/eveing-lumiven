//go:build windows

package desktop

import (
	"context"
	"crypto/rand"
	"crypto/rsa"
	"crypto/sha1"
	"crypto/tls"
	"crypto/x509"
	"crypto/x509/pkix"
	"encoding/hex"
	"encoding/json"
	"encoding/pem"
	"fmt"
	"math/big"
	"os"
	"os/exec"
	"path/filepath"
	"reflect"
	"strings"
	"syscall"
	"time"
	"unsafe"

	"golang.org/x/sys/windows"
	"golang.org/x/sys/windows/registry"
)

type RegistryValue struct {
	Name    string
	Type    uint32
	Data    []byte
	Present bool
}
type SystemLease struct {
	OriginalProxy      ProxyConfiguration  `json:"originalProxy"`
	AppliedProxy       *ProxyConfiguration `json:"appliedProxy,omitempty"`
	OwnerPID           int                 `json:"ownerPid"`
	OwnerStarted       uint64              `json:"ownerStarted"`
	Proxy              string              `json:"proxy"`
	Values             []RegistryValue     `json:"values"`
	Connections        []RegistryValue     `json:"connections"`
	Thumbprint         string              `json:"thumbprint"`
	CertificateOwned   bool                `json:"certificateOwned"`
	AppliedValues      []RegistryValue     `json:"appliedValues,omitempty"`
	AppliedConnections []RegistryValue     `json:"appliedConnections,omitempty"`
	dataDir            string
}

const settingsKey = `Software\Microsoft\Windows\CurrentVersion\Internet Settings`

func readValues(key registry.Key, names []string) ([]RegistryValue, error) {
	values := make([]RegistryValue, 0, len(names))
	for _, name := range names {
		n, kind, err := key.GetValue(name, nil)
		if err == registry.ErrNotExist {
			values = append(values, RegistryValue{Name: name})
			continue
		}
		if err != nil {
			return nil, err
		}
		data := make([]byte, n)
		_, kind, err = key.GetValue(name, data)
		if err != nil {
			return nil, err
		}
		values = append(values, RegistryValue{Name: name, Type: kind, Data: data, Present: true})
	}
	return values, nil
}

func restoreValues(key registry.Key, values []RegistryValue) error {
	proc := windows.NewLazySystemDLL("advapi32.dll").NewProc("RegSetValueExW")
	for _, v := range values {
		if !v.Present {
			if err := key.DeleteValue(v.Name); err != nil && err != registry.ErrNotExist {
				return err
			}
			continue
		}
		name, _ := windows.UTF16PtrFromString(v.Name)
		var ptr unsafe.Pointer
		if len(v.Data) > 0 {
			ptr = unsafe.Pointer(&v.Data[0])
		}
		result, _, _ := proc.Call(uintptr(key), uintptr(unsafe.Pointer(name)), 0, uintptr(v.Type), uintptr(ptr), uintptr(len(v.Data)))
		if result != 0 {
			return syscall.Errno(result)
		}
	}
	return nil
}

func hiddenPS(script string) ([]byte, error) {
	ctx, cancel := context.WithTimeout(context.Background(), 20*time.Second)
	defer cancel()
	cmd := exec.CommandContext(ctx, "powershell.exe", "-NoProfile", "-NonInteractive", "-Command", "$ErrorActionPreference='Stop'; "+script)
	cmd.SysProcAttr = &syscall.SysProcAttr{HideWindow: true, CreationFlags: 0x08000000}
	output, err := cmd.CombinedOutput()
	if ctx.Err() != nil {
		return output, fmt.Errorf("Windows证书操作超时: %w", ctx.Err())
	}
	if err != nil {
		return output, fmt.Errorf("Windows 设置失败: %s: %w", strings.TrimSpace(string(output)), err)
	}
	return output, nil
}

func saveLease(dir string, lease *SystemLease) error {
	payload, err := json.Marshal(lease)
	if err != nil {
		return err
	}
	path := filepath.Join(dir, "system-lease.json")
	if err = os.WriteFile(path+".tmp", payload, 0600); err != nil {
		return err
	}
	return os.Rename(path+".tmp", path)
}

func processIdentity(pid int) (uint64, bool, error) {
	handle, err := windows.OpenProcess(windows.PROCESS_QUERY_LIMITED_INFORMATION|windows.SYNCHRONIZE, false, uint32(pid))
	if err == windows.ERROR_INVALID_PARAMETER {
		return 0, false, nil
	}
	if err != nil {
		return 0, false, err
	}
	defer windows.CloseHandle(handle)
	var created, exited, kernel, user windows.Filetime
	if err = windows.GetProcessTimes(handle, &created, &exited, &kernel, &user); err != nil {
		return 0, false, err
	}
	result, err := windows.WaitForSingleObject(handle, 0)
	if err != nil {
		return 0, false, err
	}
	return uint64(created.HighDateTime)<<32 | uint64(created.LowDateTime), result == uint32(windows.WAIT_TIMEOUT), nil
}

// PrepareSystem records all original proxy values before changing anything.
// A unique local CA replaces the upstream shared certificate/private key.
func PrepareSystem(dir string, port int) (*SystemLease, []byte, []byte, error) {
	if err := os.MkdirAll(dir, 0700); err != nil {
		return nil, nil, nil, err
	}
	if err := RecoverSystem(dir); err != nil {
		return nil, nil, nil, err
	}
	lease := &SystemLease{Proxy: fmt.Sprintf("http=127.0.0.1:%d;https=127.0.0.1:%d", port, port), dataDir: dir}
	lease.OwnerPID = os.Getpid()
	started, _, identityErr := processIdentity(lease.OwnerPID)
	if identityErr != nil {
		return nil, nil, nil, identityErr
	}
	lease.OwnerStarted = started
	lease.OriginalProxy, identityErr = queryProxy()
	if identityErr != nil {
		return nil, nil, nil, identityErr
	}
	key, err := registry.OpenKey(registry.CURRENT_USER, settingsKey, registry.QUERY_VALUE)
	if err != nil {
		return nil, nil, nil, err
	}
	lease.Values, err = readValues(key, []string{"ProxyEnable", "ProxyServer", "ProxyOverride", "AutoConfigURL", "AutoDetect"})
	key.Close()
	if err != nil {
		return nil, nil, nil, err
	}
	key, err = registry.OpenKey(registry.CURRENT_USER, settingsKey+`\Connections`, registry.QUERY_VALUE)
	if err != nil {
		return nil, nil, nil, err
	}
	lease.Connections, err = readValues(key, []string{"DefaultConnectionSettings", "SavedLegacySettings"})
	key.Close()
	if err != nil {
		return nil, nil, nil, err
	}
	certPath, keyPath := filepath.Join(dir, "lumiven-ca.cer"), filepath.Join(dir, "lumiven-ca.key")
	certPEM, certErr := os.ReadFile(certPath)
	keyPEM, keyErr := os.ReadFile(keyPath)
	if certErr != nil || keyErr != nil {
		private, err := rsa.GenerateKey(rand.Reader, 2048)
		if err != nil {
			return nil, nil, nil, err
		}
		serial, err := rand.Int(rand.Reader, new(big.Int).Lsh(big.NewInt(1), 128))
		if err != nil {
			return nil, nil, nil, err
		}
		template := &x509.Certificate{SerialNumber: serial, Subject: pkix.Name{CommonName: "EVEING Lumiven Local Proxy", Organization: []string{"EVEING"}}, NotBefore: time.Now().Add(-time.Hour), NotAfter: time.Now().AddDate(2, 0, 0), IsCA: true, BasicConstraintsValid: true, KeyUsage: x509.KeyUsageCertSign | x509.KeyUsageDigitalSignature | x509.KeyUsageCRLSign}
		der, err := x509.CreateCertificate(rand.Reader, template, template, &private.PublicKey, private)
		if err != nil {
			return nil, nil, nil, err
		}
		certPEM = pem.EncodeToMemory(&pem.Block{Type: "CERTIFICATE", Bytes: der})
		keyPEM = pem.EncodeToMemory(&pem.Block{Type: "RSA PRIVATE KEY", Bytes: x509.MarshalPKCS1PrivateKey(private)})
		if err = os.WriteFile(keyPath, keyPEM, 0600); err != nil {
			return nil, nil, nil, err
		}
		if err = os.WriteFile(certPath, certPEM, 0600); err != nil {
			return nil, nil, nil, err
		}
	}
	block, _ := pem.Decode(certPEM)
	if block == nil {
		return nil, nil, nil, fmt.Errorf("本地证书无效")
	}
	if _, err = x509.ParseCertificate(block.Bytes); err != nil {
		return nil, nil, nil, err
	}
	if _, err = tls.X509KeyPair(certPEM, keyPEM); err != nil {
		return nil, nil, nil, fmt.Errorf("本地证书和私钥不匹配: %w", err)
	}
	hash := sha1.Sum(block.Bytes)
	lease.Thumbprint = strings.ToUpper(hex.EncodeToString(hash[:]))
	output, err := hiddenPS("if (Test-Path -LiteralPath 'Cert:\\CurrentUser\\Root\\" + lease.Thumbprint + "') { 'present' }")
	if err != nil {
		return nil, nil, nil, err
	}
	lease.CertificateOwned = strings.TrimSpace(string(output)) != "present"
	if err = saveLease(dir, lease); err != nil {
		return nil, nil, nil, err
	}
	if lease.CertificateOwned {
		_, err = hiddenPS("Import-Certificate -FilePath '" + strings.ReplaceAll(certPath, "'", "''") + "' -CertStoreLocation 'Cert:\\CurrentUser\\Root' | Out-Null")
		if err != nil {
			_ = RecoverSystem(dir)
			return nil, nil, nil, err
		}
	}
	return lease, certPEM, keyPEM, nil
}

func EnableProxy(lease *SystemLease) error {
	desired := lease.OriginalProxy
	desired.Flags = 3
	desired.Server = lease.Proxy
	desired.Bypass = "<local>"
	applyErr := setProxy(desired)
	actual, queryErr := queryProxy()
	if queryErr != nil {
		return queryErr
	}
	lease.AppliedProxy = &actual
	key, err := registry.OpenKey(registry.CURRENT_USER, settingsKey, registry.QUERY_VALUE)
	if err != nil {
		return err
	}
	lease.AppliedValues, err = readValues(key, []string{"ProxyEnable", "ProxyServer", "ProxyOverride", "AutoConfigURL", "AutoDetect"})
	key.Close()
	if err != nil {
		return err
	}
	key, err = registry.OpenKey(registry.CURRENT_USER, settingsKey+`\Connections`, registry.QUERY_VALUE)
	if err != nil {
		return err
	}
	lease.AppliedConnections, err = readValues(key, []string{"DefaultConnectionSettings", "SavedLegacySettings"})
	key.Close()
	if err != nil {
		return err
	}
	if err = saveLease(lease.dataDir, lease); err != nil {
		return err
	}
	if actual.Server != lease.Proxy || actual.Flags&2 == 0 {
		return fmt.Errorf("系统代理配置未生效")
	}
	return applyErr
}

func ownsProxySnapshot(current, applied []RegistryValue) bool {
	return reflect.DeepEqual(current, applied)
}

func RecoverSystem(dir string) error {
	path := filepath.Join(dir, "system-lease.json")
	payload, err := os.ReadFile(path)
	if os.IsNotExist(err) {
		return nil
	}
	if err != nil {
		return err
	}
	var lease SystemLease
	if err = json.Unmarshal(payload, &lease); err != nil {
		return err
	}
	if lease.OwnerPID > 0 && lease.OwnerPID != os.Getpid() {
		started, alive, err := processIdentity(lease.OwnerPID)
		if err != nil {
			return fmt.Errorf("无法确认现有内核状态: %w", err)
		}
		if alive && started == lease.OwnerStarted {
			return fmt.Errorf("现有接入内核仍在运行或收尾，请等待它退出后再恢复")
		}
	}
	if err = recoverProxy(&lease); err != nil {
		return err
	}
	if lease.CertificateOwned && len(lease.Thumbprint) == 40 {
		if _, err = hex.DecodeString(lease.Thumbprint); err != nil {
			return err
		}
		_, err = hiddenPS("$p='Cert:\\CurrentUser\\Root\\" + lease.Thumbprint + "'; if (Test-Path -LiteralPath $p) { Remove-Item -LiteralPath $p -ErrorAction SilentlyContinue }; if (Test-Path -LiteralPath $p) { throw '无法移除本软件安装的证书' }")
		if err != nil {
			return err
		}
	}
	if err = os.Remove(path); os.IsNotExist(err) {
		return nil
	}
	return err
}

func WaitProcess(pid int) error {
	handle, err := windows.OpenProcess(windows.SYNCHRONIZE, false, uint32(pid))
	if err != nil {
		return err
	}
	defer windows.CloseHandle(handle)
	_, err = windows.WaitForSingleObject(handle, windows.INFINITE)
	return err
}

func StartGuardian(dataDir string) error {
	exe, err := os.Executable()
	if err != nil {
		return err
	}
	cmd := exec.Command(exe, "--desktop-recover", "--desktop-parent", fmt.Sprint(os.Getpid()), "--desktop-data-dir", dataDir)
	cmd.SysProcAttr = &syscall.SysProcAttr{HideWindow: true, CreationFlags: 0x08000000}
	if err = cmd.Start(); err != nil {
		return err
	}
	return cmd.Process.Release()
}
