package services

import (
	"wx_channel/internal/database"
)

// Statistics 表示仪表盘统计数据
type Statistics struct {
	TotalBrowseCount   int64                     `json:"totalBrowseCount"`
	TotalDownloadCount int64                     `json:"totalDownloadCount"`
	TodayDownloadCount int64                     `json:"todayDownloadCount"`
	StorageUsed        int64                     `json:"storageUsed"`
	RecentBrowse       []database.BrowseRecord   `json:"recentBrowse"`
	RecentDownload     []database.DownloadRecord `json:"recentDownload"`
}

// ChartData 表示仪表盘的图表数据
type ChartData struct {
	Labels []string `json:"labels"`
	Values []int64  `json:"values"`
}

// StatisticsService 处理统计业务逻辑
type StatisticsService struct {
	browseRepo   *database.BrowseHistoryRepository
	downloadRepo *database.DownloadRecordRepository
}

// NewStatisticsService 创建一个新的 StatisticsService
func NewStatisticsService() *StatisticsService {
	return &StatisticsService{
		browseRepo:   database.NewBrowseHistoryRepository(),
		downloadRepo: database.NewDownloadRecordRepository(),
	}
}

// GetStatistics 返回仪表盘统计数据
// Requirements: 7.1 - 总浏览量、总下载量、今日下载量、已用存储空间
func (s *StatisticsService) GetStatistics() (*Statistics, error) {
	stats := &Statistics{}

	// 获取总浏览计数
	browseCount, err := s.browseRepo.Count()
	if err != nil {
		return nil, err
	}
	stats.TotalBrowseCount = browseCount

	// 获取总下载计数
	downloadCount, err := s.downloadRepo.Count()
	if err != nil {
		return nil, err
	}
	stats.TotalDownloadCount = downloadCount

	// 获取今天的下载计数
	todayCount, err := s.downloadRepo.CountToday()
	if err != nil {
		return nil, err
	}
	stats.TodayDownloadCount = todayCount

	// 获取已用存储空间（已完成下载的总文件大小）
	storageUsed, err := s.downloadRepo.GetTotalFileSize()
	if err != nil {
		return nil, err
	}
	stats.StorageUsed = storageUsed

	// 获取最近的浏览记录
	// Requirements: 7.3 - 最近 5 个视频
	recentBrowse, err := s.browseRepo.GetRecent(5)
	if err != nil {
		return nil, err
	}
	stats.RecentBrowse = recentBrowse

	// 获取最近的下载记录
	// Requirements: 7.4 - 最近 5 次下载
	recentDownload, err := s.downloadRepo.GetRecent(5)
	if err != nil {
		return nil, err
	}
	stats.RecentDownload = recentDownload

	return stats, nil
}

// GetChartData 返回过去 7 天的下载计数
// Requirements: 7.2 - 显示过去 7 天下载活动的图表
func (s *StatisticsService) GetChartData(days int) (*ChartData, error) {
	if days < 1 {
		days = 7
	}

	labels, values, err := s.downloadRepo.GetChartData(days)
	if err != nil {
		return nil, err
	}

	return &ChartData{
		Labels: labels,
		Values: values,
	}, nil
}

// GetRecentBrowse 获取最近的浏览记录
// Requirements: 7.3 - 仪表盘上的最近 5 个视频
func (s *StatisticsService) GetRecentBrowse(limit int) ([]database.BrowseRecord, error) {
	if limit < 1 {
		limit = 5
	}
	return s.browseRepo.GetRecent(limit)
}

// GetRecentDownload 获取最近的下载记录
// Requirements: 7.4 - 仪表盘上的最近 5 次下载
func (s *StatisticsService) GetRecentDownload(limit int) ([]database.DownloadRecord, error) {
	if limit < 1 {
		limit = 5
	}
	return s.downloadRepo.GetRecent(limit)
}
