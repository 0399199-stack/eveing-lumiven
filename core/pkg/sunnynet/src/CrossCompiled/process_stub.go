package CrossCompiled

// ProcessInfo is source compatibility only. Driver capture is unavailable.
type ProcessInfo struct {
	Pid           string
	RemoteAddress string
	RemoteProt    uint16
	V6            bool
	Id            uint64
}
