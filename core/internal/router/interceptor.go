package router

import "github.com/qtgolang/SunnyNet/SunnyNet"

// Interceptor defines a handler that can process a SunnyNet connection.
// Returns true if the request was handled and processing should stop.
type Interceptor interface {
	Handle(conn *SunnyNet.HttpConn) bool
}
