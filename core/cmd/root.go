package cmd

import (
	"crypto/hmac"
	"crypto/sha256"
	"fmt"
	"os"
	"wx_channel/internal/app"
	"wx_channel/internal/config"
	"wx_channel/internal/desktop"

	"github.com/spf13/cobra"
	"github.com/spf13/viper"
)

var (
	cfgFile         string
	port            int
	dev             string
	desktopManaged  bool
	desktopToken    string
	desktopParent   int
	desktopDataDir  string
	desktopRecover  bool
	desktopTestMode bool
)

var rootCmd = &cobra.Command{
	Use:   "lumiven-core",
	Short: "EVEING Lumiven managed download kernel",
	Long:  `Launch through EVEING Lumiven after confirming local certificate and proxy changes.`,
	Run: func(cmd *cobra.Command, args []string) {
		if desktopRecover {
			if desktopParent > 0 {
				_ = desktop.WaitProcess(desktopParent)
			}
			if err := desktop.RecoverSystem(desktopDataDir); err != nil {
				fmt.Fprintln(os.Stderr, err)
				os.Exit(1)
			}
			return
		}
		if !desktopManaged {
			fmt.Fprintln(os.Stderr, "请通过 EVEING Lumiven 界面启用下载；内核不独立安装证书或修改系统设置。")
			os.Exit(2)
		}
		cfg := config.Load()
		// 应用标志到配置
		if port != 0 {
			cfg.SetPort(port)
		}
		if desktopManaged {
			if len(desktopToken) < 32 || desktopParent <= 0 || desktopDataDir == "" {
				fmt.Fprintln(os.Stderr, "desktop mode requires a 32+ character token, parent PID and data directory")
				os.Exit(2)
			}
			cfg.DesktopManaged = true
			cfg.DesktopTestMode = desktopTestMode
			cfg.DesktopParentPID = desktopParent
			cfg.DesktopDataDir = desktopDataDir
			cfg.DesktopToken = desktopToken
			pageToken := hmac.New(sha256.New, []byte(desktopToken))
			pageToken.Write([]byte("lumiven-page-uploads"))
			cfg.SecretToken = fmt.Sprintf("%x", pageToken.Sum(nil))
			cfg.CloudEnabled = false
			cfg.RadarEnabled = false
			cfg.AutoOpenChannels = false
			cfg.SavePageSnapshot = false
			cfg.SavePageJS = false
		}

		// 创建并运行应用
		application := app.NewApp(cfg)
		application.Run()
	},
}

func Execute() {
	// 允许在 Windows 上直接双击运行（禁用 Mousetrap 检测）
	cobra.MousetrapHelpText = ""

	if err := rootCmd.Execute(); err != nil {
		fmt.Println(err)
		os.Exit(1)
	}
}

func init() {
	cobra.OnInitialize(initConfig)

	// 持久化标志
	rootCmd.PersistentFlags().StringVar(&cfgFile, "config", "", "config file (default is $HOME/.wx_channel/config.yaml)")
	rootCmd.PersistentFlags().IntVarP(&port, "port", "p", 0, "Proxy server network port")
	rootCmd.PersistentFlags().StringVarP(&dev, "dev", "d", "", "Proxy server network device")
	rootCmd.PersistentFlags().BoolVar(&desktopManaged, "desktop-managed", false, "Run under the Lumiven desktop lifecycle")
	rootCmd.PersistentFlags().BoolVar(&desktopRecover, "desktop-recover", false, "Restore Lumiven-owned local system changes")
	rootCmd.PersistentFlags().BoolVar(&desktopTestMode, "desktop-test-mode", false, "Enable authenticated fixture requests without modifying Windows")
	rootCmd.PersistentFlags().StringVar(&desktopToken, "desktop-token", "", "Local desktop authentication token")
	rootCmd.PersistentFlags().IntVar(&desktopParent, "desktop-parent", 0, "Desktop parent process ID")
	rootCmd.PersistentFlags().StringVar(&desktopDataDir, "desktop-data-dir", "", "Desktop application data directory")

	// 绑定标志到 viper
	_ = viper.BindPFlag("port", rootCmd.PersistentFlags().Lookup("port"))
	_ = viper.BindPFlag("dev", rootCmd.PersistentFlags().Lookup("dev"))
}

func initConfig() {
	if cfgFile != "" {
		viper.SetConfigFile(cfgFile)
	}
	// 配置加载由 config.Load() 在 rootCmd.Run 中完成
}
