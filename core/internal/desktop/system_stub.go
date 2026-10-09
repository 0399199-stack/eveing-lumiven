//go:build !windows

package desktop

import "errors"

type SystemLease struct {
	Thumbprint       string
	CertificateOwned bool
}

func PrepareSystem(string, int) (*SystemLease, []byte, []byte, error) {
	return nil, nil, nil, errors.New("Lumiven desktop requires Windows")
}
func EnableProxy(*SystemLease) error { return errors.New("Lumiven desktop requires Windows") }
func RecoverSystem(string) error     { return nil }
func WaitProcess(int) error          { return errors.New("unsupported") }
func StartGuardian(string) error     { return errors.New("unsupported") }
