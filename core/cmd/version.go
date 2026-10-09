package cmd

import (
	"fmt"
	"wx_channel/internal/version"

	"github.com/spf13/cobra"
)

var versionCmd = &cobra.Command{
	Use:   "version",
	Short: "打印版本信息",
	Run: func(cmd *cobra.Command, args []string) {
		fmt.Printf("wx_channel v%s\n", version.Current)
	},
}

func init() {
	rootCmd.AddCommand(versionCmd)
}
