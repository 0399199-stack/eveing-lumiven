package services

import (
	"wx_channel/internal/database"
)

// SearchResult 表示全局搜索结果
// Requirements: 12.2 - 按来源分组并显示计数
type SearchResult struct {
	BrowseResults   []database.BrowseRecord   `json:"browseResults"`
	DownloadResults []database.DownloadRecord `json:"downloadResults"`
	BrowseCount     int64                     `json:"browseCount"`
	DownloadCount   int64                     `json:"downloadCount"`
	TotalCount      int64                     `json:"totalCount"`
}

// SearchService 处理全局搜索业务逻辑
type SearchService struct {
	browseRepo   *database.BrowseHistoryRepository
	downloadRepo *database.DownloadRecordRepository
}

// NewSearchService 创建一个新的 SearchService
func NewSearchService() *SearchService {
	return &SearchService{
		browseRepo:   database.NewBrowseHistoryRepository(),
		downloadRepo: database.NewDownloadRecordRepository(),
	}
}

// Search 在浏览和下载记录中执行全局搜索
// Requirements: 12.1 - 搜索浏览和下载记录
// Requirements: 12.2 - 按来源分组并显示计数
func (s *SearchService) Search(query string, limit int) (*SearchResult, error) {
	if limit < 1 {
		limit = 20
	}

	result := &SearchResult{
		BrowseResults:   []database.BrowseRecord{},
		DownloadResults: []database.DownloadRecord{},
	}

	// 搜索浏览记录
	browseParams := &database.PaginationParams{
		Page:     1,
		PageSize: limit,
		SortBy:   "browse_time",
		SortDesc: true,
	}
	browseResult, err := s.browseRepo.Search(query, browseParams)
	if err != nil {
		return nil, err
	}
	result.BrowseResults = browseResult.Items
	result.BrowseCount = browseResult.Total

	// 搜索下载记录
	downloadParams := &database.FilterParams{
		PaginationParams: database.PaginationParams{
			Page:     1,
			PageSize: limit,
			SortBy:   "download_time",
			SortDesc: true,
		},
		Query: query,
	}
	downloadResult, err := s.downloadRepo.List(downloadParams)
	if err != nil {
		return nil, err
	}
	result.DownloadResults = downloadResult.Items
	result.DownloadCount = downloadResult.Total

	// 计算总数
	result.TotalCount = result.BrowseCount + result.DownloadCount

	return result, nil
}

// SearchBrowse 仅搜索浏览记录
func (s *SearchService) SearchBrowse(query string, params *database.PaginationParams) (*database.PagedResult[database.BrowseRecord], error) {
	if params == nil {
		params = &database.PaginationParams{
			Page:     1,
			PageSize: 20,
			SortBy:   "browse_time",
			SortDesc: true,
		}
	}
	return s.browseRepo.Search(query, params)
}

// SearchDownload 仅搜索下载记录
func (s *SearchService) SearchDownload(query string, params *database.FilterParams) (*database.PagedResult[database.DownloadRecord], error) {
	if params == nil {
		params = &database.FilterParams{
			PaginationParams: database.PaginationParams{
				Page:     1,
				PageSize: 20,
				SortBy:   "download_time",
				SortDesc: true,
			},
		}
	}
	params.Query = query
	return s.downloadRepo.List(params)
}
