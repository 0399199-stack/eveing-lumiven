package services

import (
	"fmt"
	"net/http"
	"time"

	"wx_channel/internal/version"

	"github.com/blang/semver"
	"github.com/rhysd/go-github-selfupdate/selfupdate"
)

// CheckUpdateResult 包含版本检查结果
type CheckUpdateResult struct {
	CurrentVersion string `json:"currentVersion"`
	LatestVersion  string `json:"latestVersion"`
	HasUpdate      bool   `json:"hasUpdate"`
	ReleaseNotes   string `json:"releaseNotes"`
	DownloadURL    string `json:"downloadURL"`
	PublishedAt    string `json:"publishedAt"`
}

// VersionService 处理版本检查
type VersionService struct {
	client *http.Client
}

// NewVersionService 创建一个新的 VersionService
func NewVersionService() *VersionService {
	return &VersionService{
		client: &http.Client{
			Timeout: 10 * time.Second,
		},
	}
}

// CheckUpdate 在 GitHub 上检查最新版本
func (s *VersionService) CheckUpdate() (*CheckUpdateResult, error) {
	latest, found, err := selfupdate.DetectLatest(version.Repo)
	if err != nil {
		return nil, fmt.Errorf("failed to detect latest version: %w", err)
	}

	if !found {
		return &CheckUpdateResult{
			CurrentVersion: version.Current,
			LatestVersion:  version.Current,
			HasUpdate:      false,
			ReleaseNotes:   "",
			DownloadURL:    "",
		}, nil
	}

	currentVer, err := semver.Parse(version.Current)
	if err != nil {
		return nil, fmt.Errorf("failed to parse current version: %w", err)
	}

	// latest.Version is already a semver.Version
	hasUpdate := latest.Version.GT(currentVer)

	return &CheckUpdateResult{
		CurrentVersion: version.Current,
		LatestVersion:  latest.Version.String(),
		HasUpdate:      hasUpdate,
		ReleaseNotes:   latest.ReleaseNotes,
		DownloadURL:    latest.AssetURL,
		PublishedAt:    latest.PublishedAt.Format(time.RFC3339),
	}, nil
}
