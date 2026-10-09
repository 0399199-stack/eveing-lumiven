package handlers

import (
	"errors"
	"os"
	"path/filepath"
	"testing"

	"wx_channel/internal/database"
	"wx_channel/internal/desktop"
	"wx_channel/internal/utils"
)

func TestCompleteMovedDesktopTaskKeepsExistingFileOnDatabaseFailure(t *testing.T) {
	dir := t.TempDir()
	_ = database.Close()
	if err := database.Initialize(&database.Config{DBPath: filepath.Join(dir, "records.db")}); err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() { _ = database.Close() })

	tasks, err := desktop.NewTasks(database.GetDB(), dir)
	if err != nil {
		t.Fatal(err)
	}
	id, err := tasks.Start("video-id", "video", "author", "1080p", "", 3)
	if err != nil {
		t.Fatal(err)
	}
	desired := filepath.Join(dir, "video.mp4")
	if err := os.WriteFile(desired, []byte("existing"), 0600); err != nil {
		t.Fatal(err)
	}
	incoming := filepath.Join(dir, "incoming.tmp")
	if err := os.WriteFile(incoming, []byte("new"), 0600); err != nil {
		t.Fatal(err)
	}
	moved, err := utils.MoveFileToAvailablePath(incoming, desired)
	if err != nil {
		t.Fatal(err)
	}
	if moved == desired {
		t.Fatal("move replaced an existing user file")
	}
	if _, err := database.GetDB().Exec(`DROP TABLE download_records`); err != nil {
		t.Fatal(err)
	}
	if err := (&UploadHandler{DesktopTasks: tasks}).completeMovedDesktopTask(id, moved, 3); err == nil {
		t.Fatal("database failure unexpectedly completed task")
	}
	tasks.Fail(id, errors.New("record write failed"))
	if _, err := os.Stat(moved); !os.IsNotExist(err) {
		t.Fatalf("new file was not cleaned up: %v", err)
	}
	if content, err := os.ReadFile(desired); err != nil || string(content) != "existing" {
		t.Fatalf("existing user file changed: %q, %v", content, err)
	}
	task := tasks.Snapshot()[0]
	if task.Status != "failed" || task.FilePath != "" {
		t.Fatalf("failed task appears completed: %+v", task)
	}
}
