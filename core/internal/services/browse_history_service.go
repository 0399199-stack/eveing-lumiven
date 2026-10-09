package services

import (
	"time"

	"wx_channel/internal/database"
)

// BrowseHistoryService 处理浏览历史业务逻辑
type BrowseHistoryService struct {
	repo *database.BrowseHistoryRepository
}

// NewBrowseHistoryService 创建一个新的 BrowseHistoryService
func NewBrowseHistoryService() *BrowseHistoryService {
	return &BrowseHistoryService{
		repo: database.NewBrowseHistoryRepository(),
	}
}

// Search 按标题或作者搜索浏览记录（带分页）
// Requirements: 1.3 - 在 500ms 内按标题或作者搜索
func (s *BrowseHistoryService) Search(query string, params *database.PaginationParams) (*database.PagedResult[database.BrowseRecord], error) {
	if params == nil {
		params = &database.PaginationParams{
			Page:     1,
			PageSize: 20,
			SortBy:   "browse_time",
			SortDesc: true,
		}
	}
	return s.repo.Search(query, params)
}

// List 获取浏览记录（带分页）
// Requirements: 1.1, 1.4 - 按浏览时间排序的分页列表
func (s *BrowseHistoryService) List(params *database.PaginationParams) (*database.PagedResult[database.BrowseRecord], error) {
	if params == nil {
		params = &database.PaginationParams{
			Page:     1,
			PageSize: 20,
			SortBy:   "browse_time",
			SortDesc: true,
		}
	}
	return s.repo.List(params)
}

// GetByID 按 ID 获取单条浏览记录
func (s *BrowseHistoryService) GetByID(id string) (*database.BrowseRecord, error) {
	return s.repo.GetByID(id)
}

// Clear 清空所有浏览记录
// Requirements: 5.1, 5.2 - 清空所有浏览历史（需确认）
func (s *BrowseHistoryService) Clear() error {
	return s.repo.Clear()
}

// Delete 按 ID 删除单条浏览记录
// Requirements: 5.4 - 选择性删除
func (s *BrowseHistoryService) Delete(id string) error {
	return s.repo.Delete(id)
}

// DeleteMany 按 ID 批量删除浏览记录
// Requirements: 5.4 - 选择性删除选中记录
func (s *BrowseHistoryService) DeleteMany(ids []string) (int64, error) {
	return s.repo.DeleteMany(ids)
}

// DeleteBefore 删除指定日期前的所有记录
// Requirements: 5.5 - 基于日期的清理
func (s *BrowseHistoryService) DeleteBefore(date time.Time) (int64, error) {
	return s.repo.DeleteBefore(date)
}

// Count 返回浏览记录总数
func (s *BrowseHistoryService) Count() (int64, error) {
	return s.repo.Count()
}

// GetRecent 获取最近的浏览记录
// Requirements: 7.3 - 仪表盘上的最近 5 个视频
func (s *BrowseHistoryService) GetRecent(limit int) ([]database.BrowseRecord, error) {
	return s.repo.GetRecent(limit)
}

// GetAll 获取所有浏览记录（用于导出）
// Requirements: 4.1 - 导出浏览历史
func (s *BrowseHistoryService) GetAll() ([]database.BrowseRecord, error) {
	return s.repo.GetAll()
}

// GetByIDs 按 ID 获取浏览记录（用于选择性导出）
// Requirements: 9.4 - 导出选中记录
func (s *BrowseHistoryService) GetByIDs(ids []string) ([]database.BrowseRecord, error) {
	return s.repo.GetByIDs(ids)
}

// Create 添加新的浏览记录
func (s *BrowseHistoryService) Create(record *database.BrowseRecord) error {
	return s.repo.Create(record)
}

// Update 更新现有的浏览记录
func (s *BrowseHistoryService) Update(record *database.BrowseRecord) error {
	return s.repo.Update(record)
}
