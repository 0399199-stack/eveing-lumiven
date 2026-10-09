package services

import (
	"fmt"
	"os"
	"time"

	"wx_channel/internal/database"
)

// CleanupResult 包含清理操作的结果
type CleanupResult struct {
	BrowseRecordsDeleted   int64     `json:"browseRecordsDeleted"`
	DownloadRecordsDeleted int64     `json:"downloadRecordsDeleted"`
	FilesDeleted           int64     `json:"filesDeleted"`
	SpaceFreed             int64     `json:"spaceFreed"`
	CleanupTime            time.Time `json:"cleanupTime"`
	Errors                 []string  `json:"errors,omitempty"`
}

// CleanupService 处理数据清理操作
// Requirements: 5.1, 5.2, 5.3, 5.5, 11.5
type CleanupService struct {
	browseRepo   *database.BrowseHistoryRepository
	downloadRepo *database.DownloadRecordRepository
	settingsRepo *database.SettingsRepository
}

// NewCleanupService 创建一个新的 CleanupService
func NewCleanupService() *CleanupService {
	return &CleanupService{
		browseRepo:   database.NewBrowseHistoryRepository(),
		downloadRepo: database.NewDownloadRecordRepository(),
		settingsRepo: database.NewSettingsRepository(),
	}
}

// ClearBrowseHistory 清空所有浏览历史记录
// Requirements: 5.1, 5.2 - 清空所有浏览历史（需确认）
func (s *CleanupService) ClearBrowseHistory() (*CleanupResult, error) {
	// 清空前获取计数
	count, err := s.browseRepo.Count()
	if err != nil {
		return nil, fmt.Errorf("failed to count browse records: %w", err)
	}

	// 清空所有记录
	if err := s.browseRepo.Clear(); err != nil {
		return nil, fmt.Errorf("failed to clear browse history: %w", err)
	}

	return &CleanupResult{
		BrowseRecordsDeleted: count,
		CleanupTime:          time.Now(),
	}, nil
}

// ClearDownloadRecords 清空所有下载记录（可选删除文件）
// Requirements: 5.3 - 清空下载记录（可选择删除文件）
func (s *CleanupService) ClearDownloadRecords(deleteFiles bool) (*CleanupResult, error) {
	result := &CleanupResult{
		CleanupTime: time.Now(),
		Errors:      []string{},
	}

	// 清空前获取所有记录（用于删除文件）
	records, err := s.downloadRepo.GetAll()
	if err != nil {
		return nil, fmt.Errorf("failed to get download records: %w", err)
	}

	// 如果请求则删除文件
	if deleteFiles {
		for _, record := range records {
			if record.FilePath != "" {
				fileInfo, err := os.Stat(record.FilePath)
				if err == nil {
					result.SpaceFreed += fileInfo.Size()
					if err := os.Remove(record.FilePath); err != nil {
						result.Errors = append(result.Errors, fmt.Sprintf("failed to delete file %s: %v", record.FilePath, err))
					} else {
						result.FilesDeleted++
					}
				}
			}
		}
	}

	// 清空所有记录
	if err := s.downloadRepo.Clear(); err != nil {
		return nil, fmt.Errorf("failed to clear download records: %w", err)
	}

	result.DownloadRecordsDeleted = int64(len(records))
	return result, nil
}

// DeleteBrowseRecordsBefore 删除指定日期前的浏览记录
// Requirements: 5.5 - 基于日期的清理
func (s *CleanupService) DeleteBrowseRecordsBefore(date time.Time) (*CleanupResult, error) {
	count, err := s.browseRepo.DeleteBefore(date)
	if err != nil {
		return nil, fmt.Errorf("failed to delete browse records before %v: %w", date, err)
	}

	return &CleanupResult{
		BrowseRecordsDeleted: count,
		CleanupTime:          time.Now(),
	}, nil
}

// DeleteDownloadRecordsBefore 删除指定日期前的下载记录
// Requirements: 5.5 - 基于日期的清理
func (s *CleanupService) DeleteDownloadRecordsBefore(date time.Time, deleteFiles bool) (*CleanupResult, error) {
	result := &CleanupResult{
		CleanupTime: time.Now(),
		Errors:      []string{},
	}

	// 如果需要删除文件，我们需要先获取记录
	if deleteFiles {
		// 获取所有记录并按日期过滤
		records, err := s.downloadRepo.GetAll()
		if err != nil {
			return nil, fmt.Errorf("failed to get download records: %w", err)
		}

		for _, record := range records {
			if record.DownloadTime.Before(date) && record.FilePath != "" {
				fileInfo, err := os.Stat(record.FilePath)
				if err == nil {
					result.SpaceFreed += fileInfo.Size()
					if err := os.Remove(record.FilePath); err != nil {
						result.Errors = append(result.Errors, fmt.Sprintf("failed to delete file %s: %v", record.FilePath, err))
					} else {
						result.FilesDeleted++
					}
				}
			}
		}
	}

	// 从数据库删除记录
	count, err := s.downloadRepo.DeleteBefore(date)
	if err != nil {
		return nil, fmt.Errorf("failed to delete download records before %v: %w", date, err)
	}

	result.DownloadRecordsDeleted = count
	return result, nil
}

// RunAutoCleanup 根据设置运行自动清理
// Requirements: 11.5 - 基于设置的自动清理
func (s *CleanupService) RunAutoCleanup() (*CleanupResult, error) {
	// 加载设置
	settings, err := s.settingsRepo.Load()
	if err != nil {
		return nil, fmt.Errorf("failed to load settings: %w", err)
	}

	// 检查自动清理是否启用
	if !settings.AutoCleanupEnabled {
		return &CleanupResult{
			CleanupTime: time.Now(),
		}, nil
	}

	// 计算截止日期
	cutoffDate := time.Now().AddDate(0, 0, -settings.AutoCleanupDays)

	// 删除旧的浏览记录
	browseResult, err := s.DeleteBrowseRecordsBefore(cutoffDate)
	if err != nil {
		return nil, fmt.Errorf("failed to cleanup browse records: %w", err)
	}

	return &CleanupResult{
		BrowseRecordsDeleted: browseResult.BrowseRecordsDeleted,
		CleanupTime:          time.Now(),
	}, nil
}

// DeleteSelectedBrowseRecords 按 ID 删除特定的浏览记录
// Requirements: 5.4 - 选择性删除
func (s *CleanupService) DeleteSelectedBrowseRecords(ids []string) (*CleanupResult, error) {
	count, err := s.browseRepo.DeleteMany(ids)
	if err != nil {
		return nil, fmt.Errorf("failed to delete selected browse records: %w", err)
	}

	return &CleanupResult{
		BrowseRecordsDeleted: count,
		CleanupTime:          time.Now(),
	}, nil
}

// DeleteSelectedDownloadRecords 按 ID 删除特定的下载记录
// Requirements: 5.4 - 选择性删除
func (s *CleanupService) DeleteSelectedDownloadRecords(ids []string, deleteFiles bool) (*CleanupResult, error) {
	result := &CleanupResult{
		CleanupTime: time.Now(),
		Errors:      []string{},
	}

	// 如果需要删除文件，先获取记录
	if deleteFiles {
		records, err := s.downloadRepo.GetByIDs(ids)
		if err != nil {
			return nil, fmt.Errorf("failed to get download records: %w", err)
		}

		for _, record := range records {
			if record.FilePath != "" {
				fileInfo, err := os.Stat(record.FilePath)
				if err == nil {
					result.SpaceFreed += fileInfo.Size()
					if err := os.Remove(record.FilePath); err != nil {
						result.Errors = append(result.Errors, fmt.Sprintf("failed to delete file %s: %v", record.FilePath, err))
					} else {
						result.FilesDeleted++
					}
				}
			}
		}
	}

	// 从数据库删除记录
	count, err := s.downloadRepo.DeleteMany(ids)
	if err != nil {
		return nil, fmt.Errorf("failed to delete selected download records: %w", err)
	}

	result.DownloadRecordsDeleted = count
	return result, nil
}

// CleanupAll 清空所有数据（浏览和下载记录）
// Requirements: 5.1, 5.2, 5.3 - 全面清理
func (s *CleanupService) CleanupAll(deleteFiles bool) (*CleanupResult, error) {
	result := &CleanupResult{
		CleanupTime: time.Now(),
		Errors:      []string{},
	}

	// 清空浏览历史
	browseResult, err := s.ClearBrowseHistory()
	if err != nil {
		return nil, fmt.Errorf("failed to clear browse history: %w", err)
	}
	result.BrowseRecordsDeleted = browseResult.BrowseRecordsDeleted

	// 清空下载记录
	downloadResult, err := s.ClearDownloadRecords(deleteFiles)
	if err != nil {
		return nil, fmt.Errorf("failed to clear download records: %w", err)
	}
	result.DownloadRecordsDeleted = downloadResult.DownloadRecordsDeleted
	result.FilesDeleted = downloadResult.FilesDeleted
	result.SpaceFreed = downloadResult.SpaceFreed
	result.Errors = append(result.Errors, downloadResult.Errors...)

	return result, nil
}
