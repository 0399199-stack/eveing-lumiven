package cmd

import (
	"os"

	"wx_channel/pkg/certificate"

	"github.com/fatih/color"
	"github.com/spf13/cobra"
)

var uninstallCmd = &cobra.Command{
	Use:   "uninstall",
	Short: "卸载根证书",
	Long:  `卸载 SunnyNet 根证书。如果证书未安装，无需卸载。`,
	Run: func(cmd *cobra.Command, args []string) {
		color.Yellow("正在卸载根证书...\n")

		// 检查证书是否存在
		existing, err := certificate.CheckCertificate("SunnyNet")
		if err != nil {
			color.Red("检查证书时发生错误: %v\n", err.Error())
			color.Yellow("请手动检查证书是否已安装。\n")
			os.Exit(1)
		}

		if !existing {
			color.Green("✓ 证书未安装，无需卸载。\n")
			os.Exit(0)
		}

		// 尝试卸载证书
		err = certificate.RemoveCertificate("SunnyNet")
		if err != nil {
			color.Red("卸载证书失败: %v\n", err.Error())
			color.Yellow("请尝试以管理员身份运行此命令。\n")
			os.Exit(1)
		}

		color.Green("✓ 证书卸载成功！\n")
		color.Yellow("注意：如果程序仍在运行，请重启浏览器以确保更改生效。\n")
		os.Exit(0)
	},
}
