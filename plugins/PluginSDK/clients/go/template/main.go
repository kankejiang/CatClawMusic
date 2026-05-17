package main

/*
猫爪音乐 Go 插件模板 - 歌词提供者

编译流程:
  1. 编写 main.go（本文件）
  2. 跨平台编译原生库:
     Android: GOOS=android GOARCH=arm64 CGO_ENABLED=1 go build -buildmode=c-shared -o libgoplugin.so
     Windows: go build -buildmode=c-shared -o goplugin.dll
     Linux:   go build -buildmode=c-shared -o libgoplugin.so
  3. 将 .so/.dll 放入模板目录
  4. dotnet build → GoPlugin.dll → GoPlugin.ccp

导出的 C 函数约定:
  - GetPluginVersion() → 版本字符串指针 (必须手动 FreeString)
  - GetLyricsJson(title, artist) → JSON 字符串指针 (必须手动 FreeString)
  - GetMenuItemsJson(title, artist) → JSON 字符串指针 (必须手动 FreeString)
  - FreeString(ptr) → 释放由上面函数分配的字符串

JSON 格式:
  GetLyricsJson 返回:
    {"metadata":{"title":"...","artist":"..."},"lines":[{"timestamp":"00:01.00","text":"..."}]}
    若未找到歌词, 返回 "null"

  GetMenuItemsJson 返回:
    ["10001|菜单1", "10002|菜单2"]
*/

import "C"
import (
	"encoding/json"
	"strings"
	"unsafe"
)

// PluginVersion 插件版本号
var PluginVersion = "1.0.0"

// LyricLine 歌词行
type LyricLine struct {
	Timestamp string `json:"timestamp"`
	Text      string `json:"text"`
}

// LyricMeta 歌词元数据
type LyricMeta struct {
	Title  string `json:"title"`
	Artist string `json:"artist"`
}

// LyricsResult 歌词结果
type LyricsResult struct {
	Metadata LyricMeta   `json:"metadata"`
	Lines    []LyricLine `json:"lines"`
}

//export GetPluginVersion
func GetPluginVersion() *C.char {
	return C.CString(PluginVersion)
}

//export GetLyricsJson
func GetLyricsJson(title *C.char, artist *C.char) *C.char {
	t := C.GoString(title)
	a := C.GoString(artist)

	if t == "" {
		return C.CString("null")
	}

	result := LyricsResult{
		Metadata: LyricMeta{
			Title:  t,
			Artist: a,
		},
		Lines: []LyricLine{
			{Timestamp: "00:01.00", Text: "Hello from Go Plugin SDK!"},
			{Timestamp: "00:05.00", Text: "Song: " + clean(t)},
			{Timestamp: "00:09.00", Text: "Artist: " + clean(a)},
			{Timestamp: "00:13.00", Text: "猫爪音乐 Go 插件示例"},
			{Timestamp: "00:17.00", Text: "Powered by Go c-shared on .NET CLR"},
		},
	}

	jsonBytes, err := json.Marshal(result)
	if err != nil {
		return C.CString("null")
	}
	return C.CString(string(jsonBytes))
}

//export GetMenuItemsJson
func GetMenuItemsJson(title *C.char, artist *C.char) *C.char {
	items := []string{"10001|搜索歌词 (Go)", "10002|搜索封面 (Go)"}
	jsonBytes, _ := json.Marshal(items)
	return C.CString(string(jsonBytes))
}

//export FreeString
func FreeString(s *C.char) {
	C.free(unsafe.Pointer(s))
}

func clean(s string) string {
	return strings.Map(func(r rune) rune {
		if r == '"' || r == '\\' {
			return -1
		}
		return r
	}, s)
}

func main() {}
