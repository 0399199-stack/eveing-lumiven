//go:build !windows

package utils

import "os"

func moveWithoutReplace(from, to string) error {
	if err := os.Link(from, to); err != nil {
		return err
	}
	return os.Remove(from)
}
