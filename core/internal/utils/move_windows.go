//go:build windows

package utils

import "golang.org/x/sys/windows"

// Unlike os.Rename, MoveFile never replaces an existing destination. This
// closes the race between independent downloads choosing the same filename.
func moveWithoutReplace(from, to string) error {
	source, err := windows.UTF16PtrFromString(from)
	if err != nil {
		return err
	}
	destination, err := windows.UTF16PtrFromString(to)
	if err != nil {
		return err
	}
	return windows.MoveFile(source, destination)
}
