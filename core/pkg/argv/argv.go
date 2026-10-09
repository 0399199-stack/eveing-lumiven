package argv

// https://github.com/levicook/argmapper/blob/master/argmapper.go
type Map map[string]string

func ArgsToMap(args []string) (m Map) {
	m = make(Map)
	if len(args) == 0 {
		return
	}

nextopt:
	for i, s := range args {
		// s 看起来像是一个选项吗？
		if len(s) > 1 && s[0] == '-' {
			k := ""
			v := ""

			num_minuses := 1
			if s[1] == '-' {
				num_minuses++
			}

			k = s[num_minuses:]
			if len(k) == 0 || k[0] == '-' || k[0] == '=' {
				continue nextopt
			}

			for i := 1; i < len(k); i++ { // 等号不能是第一个字符
				if k[i] == '=' {
					v = k[i+1:]
					k = k[0:i]
					break
				}
			}

			// 它必须有一个值，该值可能是下一个参数（假设下一个参数不是选项）
			remaining := args[i+1:]
			if v == "" && len(remaining) > 0 && remaining[0][0] != '-' {
				v = remaining[0]
			} // 值是下一个参数
			m[k] = v
		}
	}
	return m
}

// 获取指定参数名的值,获取失败返回默认值(多个参数名则返回最先找到的值)
func ArgsValue(margs Map, def string, keys ...string) (value string) {
	value = def // 默认值
	for _, key := range keys {
		if v, ok := margs[key]; ok && v != "" { // 找到参数
			value = v // 存储该值
			break
		}
	}
	return value
}
