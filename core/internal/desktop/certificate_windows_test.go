//go:build windows

package desktop

import (
	"bytes"
	"crypto/rand"
	"crypto/rsa"
	"crypto/sha1"
	"crypto/x509"
	"crypto/x509/pkix"
	"encoding/hex"
	"encoding/pem"
	"math/big"
	"strings"
	"testing"
	"time"

	"golang.org/x/sys/windows"
)

func testLocalCertificate(t *testing.T, name string) ([]byte, []byte, []byte) {
	t.Helper()
	key, err := rsa.GenerateKey(rand.Reader, 2048)
	if err != nil {
		t.Fatal(err)
	}
	template := &x509.Certificate{
		SerialNumber: big.NewInt(time.Now().UnixNano()),
		Subject:      pkix.Name{CommonName: name, Organization: []string{"EVEING"}},
		NotBefore:    time.Now().Add(-time.Hour), NotAfter: time.Now().Add(time.Hour),
		IsCA: true, BasicConstraintsValid: true, KeyUsage: x509.KeyUsageCertSign,
	}
	der, err := x509.CreateCertificate(rand.Reader, template, template, &key.PublicKey, key)
	if err != nil {
		t.Fatal(err)
	}
	return der, pem.EncodeToMemory(&pem.Block{Type: "CERTIFICATE", Bytes: der}),
		pem.EncodeToMemory(&pem.Block{Type: "RSA PRIVATE KEY", Bytes: x509.MarshalPKCS1PrivateKey(key)})
}

func TestOwnedCertificateDERRequiresMatchingIdentityKeyAndThumbprint(t *testing.T) {
	der, certPEM, keyPEM := testLocalCertificate(t, "EVEING Lumiven Local Proxy")
	hash := sha1.Sum(der)
	thumbprint := strings.ToUpper(hex.EncodeToString(hash[:]))
	got, err := ownedCertificateDER(certPEM, keyPEM, thumbprint)
	if err != nil || !bytes.Equal(got, der) {
		t.Fatalf("valid owned certificate rejected: %v", err)
	}
	if _, err = ownedCertificateDER(certPEM, keyPEM, strings.Repeat("0", 40)); err == nil {
		t.Fatal("mismatched lease thumbprint accepted")
	}
	_, _, otherKey := testLocalCertificate(t, "EVEING Lumiven Local Proxy")
	if _, err = ownedCertificateDER(certPEM, otherKey, thumbprint); err == nil {
		t.Fatal("unrelated private key accepted")
	}
	otherDER, otherCert, otherKey := testLocalCertificate(t, "Another Local Proxy")
	otherHash := sha1.Sum(otherDER)
	if _, err = ownedCertificateDER(otherCert, otherKey, hex.EncodeToString(otherHash[:])); err == nil {
		t.Fatal("unrelated CA subject accepted")
	}
}

func TestRemoveOwnedCertificateSkipsUnownedLease(t *testing.T) {
	if err := removeOwnedCertificate(t.TempDir(), &SystemLease{CertificateOwned: false}); err != nil {
		t.Fatalf("unowned lease accessed certificate files or store: %v", err)
	}
}

func TestDeleteMatchingCertificateOnlyChangesMemoryStore(t *testing.T) {
	store, err := windows.CertOpenStore(uintptr(windows.CERT_STORE_PROV_MEMORY), 0, 0, 0, 0)
	if err != nil {
		t.Fatal(err)
	}
	defer windows.CertCloseStore(store, 0)
	der, _, _ := testLocalCertificate(t, "EVEING Lumiven Local Proxy")
	otherDER, _, _ := testLocalCertificate(t, "Another Local Proxy")
	for _, encoded := range [][]byte{der, otherDER} {
		context, createErr := windows.CertCreateCertificateContext(windows.X509_ASN_ENCODING, &encoded[0], uint32(len(encoded)))
		if createErr != nil {
			t.Fatal(createErr)
		}
		addErr := windows.CertAddCertificateContextToStore(store, context, windows.CERT_STORE_ADD_ALWAYS, nil)
		windows.CertFreeCertificateContext(context)
		if addErr != nil {
			t.Fatal(addErr)
		}
	}
	if err = deleteMatchingCertificate(store, der); err != nil {
		t.Fatal(err)
	}
	if err = deleteMatchingCertificate(store, der); err != nil {
		t.Fatalf("retry after deletion failed: %v", err)
	}
	otherHash := sha1.Sum(otherDER)
	other, err := findCertificate(store, &otherHash)
	if err != nil || other == nil {
		t.Fatalf("unrelated certificate was removed: %v", err)
	}
	windows.CertFreeCertificateContext(other)
}
