package cmd

import (
	"fmt"
	"os"

	"github.com/blang/semver"
	"github.com/fatih/color"
	"github.com/rhysd/go-github-selfupdate/selfupdate"
	"github.com/spf13/cobra"

	"wx_channel/internal/version"
)

var updateCmd = &cobra.Command{
	Use:   "update",
	Short: "Update wx_channel to the latest version",
	Run: func(cmd *cobra.Command, args []string) {
		doUpdate()
	},
}

func doUpdate() {
	latest, found, err := selfupdate.DetectLatest(version.Repo)
	if err != nil {
		color.Red("Error occurred while detecting version: %v", err)
		return
	}

	v, err := semver.Parse(version.Current)
	if err != nil {
		color.Red("Could not parse current version '%s': %v", version.Current, err)
		return
	}

	if !found || latest.Version.LTE(v) {
		color.Green("Current version is the latest")
		return
	}

	color.Cyan("Found new version: %s", latest.Version)
	color.Cyan("Release notes:\n%s", latest.ReleaseNotes)
	fmt.Print("Do you want to update? (y/n): ")

	var input string
	fmt.Scanln(&input)
	if input != "y" && input != "Y" {
		color.Yellow("Update cancelled")
		return
	}

	exe, err := os.Executable()
	if err != nil {
		color.Red("Could not locate executable path")
		return
	}

	if err := selfupdate.UpdateTo(latest.AssetURL, exe); err != nil {
		color.Red("Error occurred while updating binary: %v", err)
		return
	}

	color.Green("Successfully updated to version %s", latest.Version)
}
