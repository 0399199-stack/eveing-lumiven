//go:build windows

package desktop

import (
	"fmt"
	"golang.org/x/sys/windows"
	"golang.org/x/sys/windows/registry"
	"runtime"
	"unsafe"
)

// The layouts and option IDs follow Microsoft's INTERNET_PER_CONN_OPTIONW and
// INTERNET_PER_CONN_OPTION_LISTW. Only the current user's default connection is used.
type ProxyConfiguration struct {
	Flags      uint32 `json:"flags"`
	Server     string `json:"server"`
	Bypass     string `json:"bypass"`
	AutoConfig string `json:"autoConfig"`
}
type connectionOption struct {
	Option uint32
	Value  uintptr
}
type connectionOptionList struct {
	Size       uint32
	Connection *uint16
	Count      uint32
	Error      uint32
	Options    *connectionOption
}

func queryProxy() (ProxyConfiguration, error) {
	options := []connectionOption{{Option: 10}, {Option: 2}, {Option: 3}, {Option: 4}}
	list := connectionOptionList{Size: uint32(unsafe.Sizeof(connectionOptionList{})), Count: uint32(len(options)), Options: &options[0]}
	size := list.Size
	query := windows.NewLazySystemDLL("wininet.dll").NewProc("InternetQueryOptionW")
	result, _, err := query.Call(0, 75, uintptr(unsafe.Pointer(&list)), uintptr(unsafe.Pointer(&size)))
	if result == 0 {
		return ProxyConfiguration{}, fmt.Errorf("读取当前用户代理配置失败: %w", err)
	}
	text := func(value uintptr) string {
		if value == 0 {
			return ""
		}
		return windows.UTF16PtrToString((*uint16)(unsafe.Pointer(value)))
	}
	config := ProxyConfiguration{Flags: uint32(options[0].Value), Server: text(options[1].Value), Bypass: text(options[2].Value), AutoConfig: text(options[3].Value)}
	free := windows.NewLazySystemDLL("kernel32.dll").NewProc("GlobalFree")
	for _, option := range options[1:] {
		if option.Value != 0 {
			free.Call(option.Value)
		}
	}
	runtime.KeepAlive(list)
	runtime.KeepAlive(options)
	return config, nil
}

func setProxy(config ProxyConfiguration) error {
	server, err := windows.UTF16PtrFromString(config.Server)
	if err != nil {
		return err
	}
	bypass, err := windows.UTF16PtrFromString(config.Bypass)
	if err != nil {
		return err
	}
	auto, err := windows.UTF16PtrFromString(config.AutoConfig)
	if err != nil {
		return err
	}
	options := []connectionOption{{Option: 1, Value: uintptr(config.Flags)}, {Option: 2, Value: uintptr(unsafe.Pointer(server))}, {Option: 3, Value: uintptr(unsafe.Pointer(bypass))}, {Option: 4, Value: uintptr(unsafe.Pointer(auto))}}
	list := connectionOptionList{Size: uint32(unsafe.Sizeof(connectionOptionList{})), Count: uint32(len(options)), Options: &options[0]}
	proc := windows.NewLazySystemDLL("wininet.dll").NewProc("InternetSetOptionW")
	result, _, err := proc.Call(0, 75, uintptr(unsafe.Pointer(&list)), uintptr(list.Size))
	runtime.KeepAlive(list)
	runtime.KeepAlive(options)
	runtime.KeepAlive(server)
	runtime.KeepAlive(bypass)
	runtime.KeepAlive(auto)
	if result == 0 {
		return fmt.Errorf("恢复当前用户代理配置失败: %w", err)
	}
	if result, _, err = proc.Call(0, 95, 0, 0); result == 0 {
		return err
	}
	if result, _, err = proc.Call(0, 37, 0, 0); result == 0 {
		return err
	}
	return nil
}

func recoverProxy(lease *SystemLease) error {
	current, err := queryProxy()
	if err != nil {
		return err
	}
	applied := ProxyConfiguration{Flags: 3, Server: lease.Proxy, Bypass: "<local>", AutoConfig: lease.OriginalProxy.AutoConfig}
	if lease.AppliedProxy != nil {
		applied = *lease.AppliedProxy
	}
	merged, owned := mergeProxyRecovery(current, lease.OriginalProxy, applied, lease.Proxy)
	if !owned {
		return nil
	}
	key, err := registry.OpenKey(registry.CURRENT_USER, settingsKey, registry.QUERY_VALUE|registry.SET_VALUE)
	if err != nil {
		return err
	}
	defer key.Close()
	values, err := readValues(key, []string{"ProxyEnable", "ProxyServer", "ProxyOverride", "AutoConfigURL", "AutoDetect"})
	if err != nil {
		return err
	}
	connectionKey, err := registry.OpenKey(registry.CURRENT_USER, settingsKey+`\Connections`, registry.QUERY_VALUE|registry.SET_VALUE)
	if err != nil {
		return err
	}
	defer connectionKey.Close()
	connections, err := readValues(connectionKey, []string{"DefaultConnectionSettings", "SavedLegacySettings"})
	if err != nil {
		return err
	}
	unchanged := current == applied && ownsProxySnapshot(values, lease.AppliedValues) && ownsProxySnapshot(connections, lease.AppliedConnections)
	if err = setProxy(merged); err != nil {
		return err
	}
	if unchanged {
		if err = restoreValues(key, lease.Values); err != nil {
			return err
		}
		if err = restoreValues(connectionKey, lease.Connections); err != nil {
			return err
		}
		proc := windows.NewLazySystemDLL("wininet.dll").NewProc("InternetSetOptionW")
		if result, _, err := proc.Call(0, 95, 0, 0); result == 0 {
			return err
		}
		if result, _, err := proc.Call(0, 37, 0, 0); result == 0 {
			return err
		}
	}
	verified, err := queryProxy()
	if err != nil {
		return err
	}
	if verified != merged || verified.Server == lease.Proxy {
		return fmt.Errorf("系统代理未完全恢复，请在Windows代理设置中检查；恢复记录已保留")
	}
	return nil
}

func mergeProxyRecovery(current, original, applied ProxyConfiguration, ownedServer string) (ProxyConfiguration, bool) {
	if current.Server != ownedServer {
		return current, false
	}
	merged := current
	merged.Server = original.Server
	if current.Flags == applied.Flags {
		merged.Flags = original.Flags
	} else if current.Flags&2 == applied.Flags&2 {
		merged.Flags = (current.Flags &^ 2) | (original.Flags & 2)
	}
	if current.Bypass == applied.Bypass {
		merged.Bypass = original.Bypass
	}
	if current.AutoConfig == applied.AutoConfig {
		merged.AutoConfig = original.AutoConfig
	}
	return merged, true
}
