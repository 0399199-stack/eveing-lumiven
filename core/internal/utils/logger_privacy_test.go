package utils

import (
	"bytes"
	"fmt"
	"strings"
	"testing"

	"github.com/rs/zerolog"
)

func TestDownloadLogsOmitSignedURLAndErrorDetails(t *testing.T) {
	var output bytes.Buffer
	previous := defaultLogger
	defaultLogger = &Logger{zLogger: zerolog.New(&output)}
	t.Cleanup(func() { defaultLogger = previous })

	signedURL := "https://user:password@cdn.example.test/private/video.mp4?token=secret-token&key=decrypt-key#fragment"
	failure := fmt.Errorf("GET %s: status 403 key=another-secret", signedURL)
	LogDownload("task-123", "测试", "作者", signedURL, 1024, true)
	LogDownloadError("task-123", "测试", "作者", signedURL, failure, 2)
	LogDownloadRetry("task-123", "测试", 1, 2, failure)

	logged := output.String()
	for _, secret := range []string{"password", "/private/video.mp4", "secret-token", "decrypt-key", "another-secret", "fragment"} {
		if strings.Contains(logged, secret) {
			t.Fatalf("download log contains private URL or error detail: %q", secret)
		}
	}
	for _, expected := range []string{"cdn.example.test", "task-123", "error_type"} {
		if !strings.Contains(logged, expected) {
			t.Fatalf("download log missing diagnostic field %q", expected)
		}
	}
}
