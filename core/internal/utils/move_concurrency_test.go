package utils

import (
	"fmt"
	"os"
	"path/filepath"
	"sync"
	"testing"
)

func TestConcurrentDownloadsDoNotReplaceSameFilename(t *testing.T) {
	dir := t.TempDir()
	var workers sync.WaitGroup
	paths := make(chan string, 5)
	errs := make(chan error, 5)
	for i := 0; i < 5; i++ {
		source := filepath.Join(dir, fmt.Sprintf("%d.tmp", i))
		os.WriteFile(source, []byte(fmt.Sprint(i)), 0600)
		workers.Add(1)
		go func() {
			defer workers.Done()
			path, err := MoveFileToAvailablePath(source, filepath.Join(dir, "同名视频.mp4"))
			if err != nil {
				errs <- err
				return
			}
			paths <- path
		}()
	}
	workers.Wait()
	close(paths)
	close(errs)
	for err := range errs {
		t.Fatal(err)
	}
	unique := map[string]bool{}
	contents := map[string]bool{}
	for path := range paths {
		unique[path] = true
		data, err := os.ReadFile(path)
		if err != nil {
			t.Fatal(err)
		}
		contents[string(data)] = true
	}
	if len(unique) != 5 || len(contents) != 5 {
		t.Fatalf("overwritten files: %d paths / %d contents", len(unique), len(contents))
	}
}
