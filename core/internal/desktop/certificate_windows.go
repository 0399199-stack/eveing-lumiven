//go:build windows

package desktop

import (
	"bytes"
	"crypto/sha1"
	"crypto/tls"
	"crypto/x509"
	"encoding/hex"
	"encoding/pem"
	"fmt"
	"os"
	"path/filepath"
	"runtime"
	"strings"
	"syscall"
	"unsafe"

	"golang.org/x/sys/windows"
)

func ownedCertificateDER(certPEM, keyPEM []byte, thumbprint string) ([]byte, error) {
	if len(thumbprint) != 40 {
		return nil, fmt.Errorf("本地证书指纹无效")
	}
	if _, err := hex.DecodeString(thumbprint); err != nil {
		return nil, fmt.Errorf("本地证书指纹无效: %w", err)
	}
	block, _ := pem.Decode(certPEM)
	if block == nil || block.Type != "CERTIFICATE" {
		return nil, fmt.Errorf("本地证书文件无效")
	}
	cert, err := x509.ParseCertificate(block.Bytes)
	if err != nil {
		return nil, err
	}
	if !cert.IsCA || cert.Subject.CommonName != "EVEING Lumiven Local Proxy" ||
		len(cert.Subject.Organization) != 1 || cert.Subject.Organization[0] != "EVEING" || cert.CheckSignatureFrom(cert) != nil {
		return nil, fmt.Errorf("证书不是本软件生成的本地根证书")
	}
	if _, err = tls.X509KeyPair(certPEM, keyPEM); err != nil {
		return nil, fmt.Errorf("本地证书和私钥不匹配: %w", err)
	}
	hash := sha1.Sum(block.Bytes)
	if !strings.EqualFold(hex.EncodeToString(hash[:]), thumbprint) {
		return nil, fmt.Errorf("本地证书与接入记录不匹配")
	}
	return block.Bytes, nil
}

func findCertificate(store windows.Handle, hash *[sha1.Size]byte) (*windows.CertContext, error) {
	blob := windows.CryptHashBlob{Size: sha1.Size, Data: &hash[0]}
	cert, err := windows.CertFindCertificateInStore(store, windows.X509_ASN_ENCODING|windows.PKCS_7_ASN_ENCODING,
		0, windows.CERT_FIND_SHA1_HASH, unsafe.Pointer(&blob), nil)
	if err == syscall.Errno(windows.CRYPT_E_NOT_FOUND) {
		return nil, nil
	}
	return cert, err
}

func deleteMatchingCertificate(store windows.Handle, der []byte) error {
	hash := sha1.Sum(der)
	context, err := findCertificate(store, &hash)
	if err != nil || context == nil {
		return err
	}
	if context.EncodedCert == nil || int(context.Length) != len(der) ||
		!bytes.Equal(unsafe.Slice(context.EncodedCert, int(context.Length)), der) {
		windows.CertFreeCertificateContext(context)
		return fmt.Errorf("证书库中的证书与本软件证书文件不匹配")
	}
	// CertDeleteCertificateFromStore frees context even when deletion fails.
	if err = windows.CertDeleteCertificateFromStore(context); err != nil {
		return fmt.Errorf("移除本软件根证书失败: %w", err)
	}
	context, err = findCertificate(store, &hash)
	if context != nil {
		windows.CertFreeCertificateContext(context)
		return fmt.Errorf("本软件根证书仍在当前用户证书库")
	}
	return err
}

// removeOwnedCertificate touches only the current user's physical Root store.
// The logical Root store also includes machine roots and must not be used here.
func removeOwnedCertificate(dir string, lease *SystemLease) error {
	if !lease.CertificateOwned {
		return nil
	}
	certPEM, err := os.ReadFile(filepath.Join(dir, "lumiven-ca.cer"))
	if err != nil {
		return err
	}
	keyPEM, err := os.ReadFile(filepath.Join(dir, "lumiven-ca.key"))
	if err != nil {
		return err
	}
	der, err := ownedCertificateDER(certPEM, keyPEM, lease.Thumbprint)
	if err != nil {
		return err
	}
	name, err := windows.UTF16PtrFromString("Root")
	if err != nil {
		return err
	}
	store, err := windows.CertOpenStore(uintptr(windows.CERT_STORE_PROV_SYSTEM_REGISTRY_W), 0, 0,
		windows.CERT_SYSTEM_STORE_CURRENT_USER|windows.CERT_STORE_OPEN_EXISTING_FLAG, uintptr(unsafe.Pointer(name)))
	runtime.KeepAlive(name)
	if err != nil {
		return fmt.Errorf("打开当前用户根证书库失败: %w", err)
	}
	defer windows.CertCloseStore(store, 0)
	return deleteMatchingCertificate(store, der)
}
