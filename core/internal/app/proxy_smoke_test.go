package app

import (
	"crypto/rand"
	"crypto/rsa"
	"crypto/tls"
	"crypto/x509"
	"encoding/pem"
	"io"
	"math/big"
	"net"
	"net/http"
	"net/http/httptest"
	"net/url"
	"testing"
	"time"

	"github.com/qtgolang/SunnyNet/SunnyNet"
	"github.com/qtgolang/SunnyNet/src/Certificate"
)

func TestLoopbackProxyWorksWithoutProcessDriver(t *testing.T) {
	upstream := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, _ *http.Request) { w.Write([]byte("http-ok")) }))
	defer upstream.Close()
	secure := httptest.NewTLSServer(http.HandlerFunc(func(w http.ResponseWriter, _ *http.Request) { w.Write([]byte("https-ok")) }))
	defer secure.Close()

	probe, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		t.Fatal(err)
	}
	address := probe.Addr().(*net.TCPAddr)
	probe.Close()
	proxy := SunnyNet.NewSunny().SetPort(address.Port)
	private, err := rsa.GenerateKey(rand.Reader, 2048)
	if err != nil {
		t.Fatal(err)
	}
	template := &x509.Certificate{SerialNumber: big.NewInt(time.Now().UnixNano()), NotBefore: time.Now().Add(-time.Hour), NotAfter: time.Now().Add(time.Hour), IsCA: true, BasicConstraintsValid: true, KeyUsage: x509.KeyUsageCertSign | x509.KeyUsageDigitalSignature}
	der, err := x509.CreateCertificate(rand.Reader, template, template, &private.PublicKey, private)
	if err != nil {
		t.Fatal(err)
	}
	certPEM := pem.EncodeToMemory(&pem.Block{Type: "CERTIFICATE", Bytes: der})
	keyPEM := pem.EncodeToMemory(&pem.Block{Type: "RSA PRIVATE KEY", Bytes: x509.MarshalPKCS1PrivateKey(private)})
	managerID := Certificate.CreateCertificate()
	manager := Certificate.LoadCertificateContext(managerID)
	if manager == nil || !manager.LoadX509Certificate("", string(certPEM), string(keyPEM)) {
		t.Fatal("isolated proxy certificate could not load")
	}
	proxy.SetCert(managerID)
	if err = proxy.Start().Error; err != nil {
		t.Fatal(err)
	}
	defer proxy.Close()

	proxyURL := &url.URL{Scheme: "http", Host: address.String()}
	transport := &http.Transport{Proxy: http.ProxyURL(proxyURL), TLSClientConfig: &tls.Config{InsecureSkipVerify: true}} // Isolated fixture does not install a test CA.
	defer transport.CloseIdleConnections()
	client := &http.Client{Transport: transport, Timeout: 5 * time.Second}
	for target, want := range map[string]string{upstream.URL: "http-ok", secure.URL: "https-ok"} {
		response, err := client.Get(target)
		if err != nil {
			t.Fatalf("proxy GET %s: %v", target, err)
		}
		body, err := io.ReadAll(response.Body)
		response.Body.Close()
		if err != nil || response.StatusCode != 200 || string(body) != want {
			t.Fatalf("proxy GET %s: status=%d body=%q error=%v", target, response.StatusCode, body, err)
		}
	}
}
