//go:build windows

package desktop

import (
	"crypto/rand"
	"crypto/rsa"
	"crypto/sha1"
	"crypto/x509"
	"crypto/x509/pkix"
	"encoding/hex"
	"encoding/pem"
	"math/big"
	"os"
	"path/filepath"
	"strings"
	"testing"
	"time"
)

func TestCertificateStoreCommandsCanPreviewWithoutChangingTrust(t *testing.T) {
	key, err := rsa.GenerateKey(rand.Reader, 2048)
	if err != nil {
		t.Fatal(err)
	}
	template := &x509.Certificate{SerialNumber: big.NewInt(time.Now().UnixNano()), Subject: pkix.Name{CommonName: "Lumiven preview test"}, NotBefore: time.Now().Add(-time.Hour), NotAfter: time.Now().Add(time.Hour), IsCA: true, BasicConstraintsValid: true, KeyUsage: x509.KeyUsageCertSign}
	der, err := x509.CreateCertificate(rand.Reader, template, template, &key.PublicKey, key)
	if err != nil {
		t.Fatal(err)
	}
	certPath := filepath.Join(t.TempDir(), "preview.cer")
	if err = os.WriteFile(certPath, pem.EncodeToMemory(&pem.Block{Type: "CERTIFICATE", Bytes: der}), 0600); err != nil {
		t.Fatal(err)
	}
	hash := sha1.Sum(der)
	thumbprint := strings.ToUpper(hex.EncodeToString(hash[:]))
	storePath := "Cert:\\CurrentUser\\Root\\" + thumbprint
	if _, err = runPS("Import-Certificate -FilePath '"+strings.ReplaceAll(certPath, "'", "''")+"' -CertStoreLocation 'Cert:\\CurrentUser\\Root' -WhatIf | Out-Null", true); err != nil {
		t.Fatalf("certificate import preview failed: %v", err)
	}
	output, err := runPS("Test-Path -LiteralPath '"+storePath+"'", false)
	if err != nil || strings.TrimSpace(string(output)) != "False" {
		t.Fatalf("import preview changed trust store: %q, %v", output, err)
	}
}

func TestCreateLocalCARotatesAppOwnedCertificateAndKey(t *testing.T) {
	dir := t.TempDir()
	certPath, keyPath := filepath.Join(dir, "lumiven-ca.cer"), filepath.Join(dir, "lumiven-ca.key")
	firstCert, firstKey, err := createLocalCA(certPath, keyPath)
	if err != nil {
		t.Fatal(err)
	}
	firstThumbprint, err := localCAThumbprint(firstCert, firstKey)
	if err != nil {
		t.Fatal(err)
	}
	secondCert, secondKey, err := createLocalCA(certPath, keyPath)
	if err != nil {
		t.Fatal(err)
	}
	secondThumbprint, err := localCAThumbprint(secondCert, secondKey)
	if err != nil {
		t.Fatal(err)
	}
	if firstThumbprint == secondThumbprint || string(firstKey) == string(secondKey) {
		t.Fatal("rotated local CA reused its certificate or private key")
	}
	if _, err = ownedCertificateDER(secondCert, secondKey, secondThumbprint); err != nil {
		t.Fatalf("rotated local CA is not recognized as app-owned: %v", err)
	}
	onDiskCert, err := os.ReadFile(certPath)
	if err != nil {
		t.Fatal(err)
	}
	onDiskKey, err := os.ReadFile(keyPath)
	if err != nil {
		t.Fatal(err)
	}
	if string(onDiskCert) != string(secondCert) || string(onDiskKey) != string(secondKey) {
		t.Fatal("rotated local CA files do not match the returned certificate and key")
	}
}

func TestPrepareLocalCARepairsMismatchOnlyWhenCertificateIsUntrusted(t *testing.T) {
	for _, partialWrite := range []string{"certificate-first", "key-first"} {
		for _, certificateTrusted := range []bool{false, true} {
			t.Run(partialWrite+"/"+map[bool]string{false: "untrusted", true: "trusted"}[certificateTrusted], func(t *testing.T) {
				dir := t.TempDir()
				certPath, keyPath := filepath.Join(dir, "lumiven-ca.cer"), filepath.Join(dir, "lumiven-ca.key")
				oldCert, oldKey, err := createLocalCA(certPath, keyPath)
				if err != nil {
					t.Fatal(err)
				}
				newCert, newKey, err := createLocalCA(certPath, keyPath)
				if err != nil {
					t.Fatal(err)
				}
				mismatchedCert, mismatchedKey := newCert, oldKey
				if partialWrite == "key-first" {
					mismatchedCert, mismatchedKey = oldCert, newKey
				}
				_, mismatchedThumbprint, err := ownedCertificateInfo(mismatchedCert)
				if err != nil {
					t.Fatal(err)
				}
				if err = os.WriteFile(certPath, mismatchedCert, 0600); err != nil {
					t.Fatal(err)
				}
				if err = os.WriteFile(keyPath, mismatchedKey, 0600); err != nil {
					t.Fatal(err)
				}
				certPEM, keyPEM, thumbprint, owned, err := prepareLocalCA(certPath, keyPath, func(candidate string) (bool, error) {
					return candidate == mismatchedThumbprint && certificateTrusted, nil
				})
				if certificateTrusted {
					if err == nil {
						t.Fatal("trusted certificate with a mismatched key was overwritten")
					}
					onDiskCert, certErr := os.ReadFile(certPath)
					onDiskKey, keyErr := os.ReadFile(keyPath)
					if certErr != nil || keyErr != nil || string(onDiskCert) != string(mismatchedCert) || string(onDiskKey) != string(mismatchedKey) {
						t.Fatal("trusted certificate or mismatched key changed")
					}
					return
				}
				if err != nil || !owned || thumbprint == mismatchedThumbprint {
					t.Fatalf("untrusted mismatch was not repaired: thumbprint=%s owned=%v err=%v", thumbprint, owned, err)
				}
				if _, err = ownedCertificateDER(certPEM, keyPEM, thumbprint); err != nil {
					t.Fatalf("repaired pair is invalid: %v", err)
				}
				onDiskCert, certErr := os.ReadFile(certPath)
				onDiskKey, keyErr := os.ReadFile(keyPath)
				if certErr != nil || keyErr != nil || string(onDiskCert) != string(certPEM) || string(onDiskKey) != string(keyPEM) {
					t.Fatal("repaired files do not match the returned certificate and key")
				}
			})
		}
	}
}

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
