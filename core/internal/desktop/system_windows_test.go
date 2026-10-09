//go:build windows

package desktop

import (
	"os"
	"strings"
	"testing"
)

func TestProxyOwnershipRequiresUnchangedAppliedSettings(t *testing.T) {
	ours := []RegistryValue{{Name: "ProxyServer", Type: 1, Data: []byte("127.0.0.1:2025"), Present: true}, {Name: "ProxyEnable", Type: 4, Data: []byte{1, 0, 0, 0}, Present: true}, {Name: "ProxyOverride", Type: 1, Data: []byte("<local>"), Present: true}}
	if !ownsProxySnapshot(ours, ours) {
		t.Fatal("owned snapshot rejected")
	}
	userChanged := append([]RegistryValue(nil), ours...)
	userChanged[2].Data = []byte("user.example")
	if ownsProxySnapshot(userChanged, ours) {
		t.Fatal("user bypass change would be overwritten")
	}
	userChanged = append([]RegistryValue(nil), ours...)
	userChanged[1].Data = []byte{0, 0, 0, 0}
	if ownsProxySnapshot(userChanged, ours) {
		t.Fatal("user disabled proxy would be overwritten")
	}
}

func TestRecoveryRefusesLivingDifferentProcessBeforeOpeningRegistry(t *testing.T) {
	started, alive, err := processIdentity(os.Getppid())
	if err != nil || !alive {
		t.Skip("parent unavailable")
	}
	dir := t.TempDir()
	lease := &SystemLease{OwnerPID: os.Getppid(), OwnerStarted: started}
	if err = saveLease(dir, lease); err != nil {
		t.Fatal(err)
	}
	err = RecoverSystem(dir)
	if err == nil || !strings.Contains(err.Error(), "仍在运行") {
		t.Fatalf("living owner was not protected: %v", err)
	}
}

func TestProxyRecoveryPreservesUserEditsWithoutLeavingDeadProxy(t *testing.T) {
	original := ProxyConfiguration{Flags: 9, Server: "", Bypass: "previous.local", AutoConfig: "https://example.test/pac"}
	applied := ProxyConfiguration{Flags: 3, Server: "http=127.0.0.1:2025;https=127.0.0.1:2025", Bypass: "<local>", AutoConfig: original.AutoConfig}
	tests := []struct {
		name    string
		current ProxyConfiguration
		want    ProxyConfiguration
		owned   bool
	}{
		{"unchanged", applied, original, true},
		{"bypass edited", ProxyConfiguration{Flags: 3, Server: applied.Server, Bypass: "user.local", AutoConfig: original.AutoConfig}, ProxyConfiguration{Flags: 9, Server: "", Bypass: "user.local", AutoConfig: original.AutoConfig}, true},
		{"user disabled", ProxyConfiguration{Flags: 1, Server: applied.Server, Bypass: applied.Bypass, AutoConfig: original.AutoConfig}, ProxyConfiguration{Flags: 1, Server: "", Bypass: original.Bypass, AutoConfig: original.AutoConfig}, true},
		{"PAC edited", ProxyConfiguration{Flags: 7, Server: applied.Server, Bypass: applied.Bypass, AutoConfig: "https://user.test/pac"}, ProxyConfiguration{Flags: 5, Server: "", Bypass: original.Bypass, AutoConfig: "https://user.test/pac"}, true},
		{"other proxy", ProxyConfiguration{Flags: 3, Server: "127.0.0.1:7890"}, ProxyConfiguration{Flags: 3, Server: "127.0.0.1:7890"}, false},
	}
	for _, test := range tests {
		t.Run(test.name, func(t *testing.T) {
			got, owned := mergeProxyRecovery(test.current, original, applied, applied.Server)
			if got != test.want || owned != test.owned {
				t.Fatalf("got %+v owned %v, want %+v owned %v", got, owned, test.want, test.owned)
			}
			if owned && got.Server == applied.Server {
				t.Fatal("Lumiven dead proxy left behind")
			}
		})
	}
}

func TestReadOnlyProxyConfigurationUsesValidWindowsLayout(t *testing.T) {
	config, err := queryProxy()
	if err != nil {
		t.Fatal(err)
	}
	if config.Flags > 15 {
		t.Fatalf("invalid proxy flags: %d", config.Flags)
	}
}
