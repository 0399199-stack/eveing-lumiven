package utils

import (
	"context"
	"io"
	"sync"
	"time"
)

// ProgressReader wraps an io.Reader to track progress
type ProgressReader struct {
	Ctx        context.Context // Context for cancellation
	Reader     io.Reader
	Total      int64
	Current    int64
	LastLog    time.Time
	OnProgress func(current, total int64)
	mu         sync.Mutex
}

// Read implements io.Reader
func (pr *ProgressReader) Read(p []byte) (int, error) {
	// Check context cancellation first
	if pr.Ctx != nil {
		select {
		case <-pr.Ctx.Done():
			return 0, pr.Ctx.Err()
		default:
		}
	}

	n, err := pr.Reader.Read(p)

	pr.mu.Lock()
	pr.Current += int64(n)
	current := pr.Current
	lastLog := pr.LastLog
	pr.mu.Unlock()

	// Throttle updates: Max once per second
	if pr.OnProgress != nil {
		now := time.Now()
		if now.Sub(lastLog) >= 1*time.Second || err != nil || (pr.Total > 0 && current == pr.Total) {
			pr.mu.Lock()
			pr.LastLog = now
			pr.mu.Unlock()
			pr.OnProgress(current, pr.Total)
		}
	}

	return n, err
}

// Close implements io.Closer
func (pr *ProgressReader) Close() error {
	if c, ok := pr.Reader.(io.Closer); ok {
		return c.Close()
	}
	return nil
}
