package router

import (
	"bytes"
	"net/http"

	"github.com/qtgolang/SunnyNet/SunnyNet"
)

// SunnyNetResponseWriter adapts SunnyNet.HttpConn to http.ResponseWriter
type SunnyNetResponseWriter struct {
	conn       *SunnyNet.HttpConn
	headers    http.Header
	statusCode int
	body       bytes.Buffer
}

// NewSunnyNetResponseWriter creates a new SunnyNetResponseWriter
func NewSunnyNetResponseWriter(conn *SunnyNet.HttpConn) *SunnyNetResponseWriter {
	return &SunnyNetResponseWriter{
		conn:       conn,
		headers:    make(http.Header),
		statusCode: http.StatusOK,
	}
}

func (w *SunnyNetResponseWriter) Header() http.Header {
	return w.headers
}

func (w *SunnyNetResponseWriter) Write(data []byte) (int, error) {
	return w.body.Write(data)
}

func (w *SunnyNetResponseWriter) WriteHeader(statusCode int) {
	w.statusCode = statusCode
}

// Flush sends the response to SunnyNet
func (w *SunnyNetResponseWriter) Flush() {
	// SunnyNet's StopRequest will interrupt the processing and send the response
	w.conn.StopRequest(w.statusCode, w.body.String(), w.headers)
}
