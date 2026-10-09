package SunnyNet

// Lumiven only supports HTTP/HTTPS CONNECT. Driver forwarding and SOCKS UDP
// are deliberately excluded from this independently redistributable build.
func (s *Sunny) listenUdpGo() {
	buffer := make([]byte, 65536)
	for {
		if _, _, err := s.udpSocket.ReadFromUDP(buffer); err != nil {
			return
		}
	}
}
func (s *Sunny) udpNFSendReceive(kind int8, id int64, pid uint32, local, remote string, data []byte) []byte {
	if s.goUdpCallback == nil {
		return data
	}
	conn := &UDPConn{Theology: id, MessageId: NewMessageId(), Type: kind, SunnyContext: s.SunnyContext, Pid: int(pid), LocalAddress: local, RemoteAddress: remote, Data: data}
	s.goUdpCallback(conn)
	return conn.Data
}
