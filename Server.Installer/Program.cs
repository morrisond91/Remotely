﻿using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Remotely.Shared.Enums;
using Remotely.Shared.Services;
using Remotely.Shared.Utilities;
using Server.Installer.Models;
using Server.Installer.Services;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Server.Installer
{

    public class Program
    {
        public static IServiceProvider Services { get; set; }

        public static async Task Main(string[] args)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine(AppConstants.RemotelyAscii);
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine("(https://remotely.one)");
            Console.WriteLine();
            Console.WriteLine();


            if (!ParseCliParams(args, out var cliParams))
            {
                ShowHelpText();
                return;
            }

            if (EnvironmentHelper.Platform != Platform.Windows &&
                EnvironmentHelper.Platform != Platform.Linux)
            {
                ConsoleHelper.WriteError("Remotely Server can only be installed on Linux or Windows.");
                return;
            }

            BuildServices();

            var elevationDetector = Services.GetRequiredService<IElevationDetector>();

            if (!elevationDetector.IsElevated())
            {
                ConsoleHelper.WriteError("The installer process must be elevated.  On Linux, run with sudo.  " +
                    "On Windows, run from a command line that was opened with \"Run as admin\".");
                ConsoleHelper.ReadLine("Press any key to exit.");
                return;
            }

            ConsoleHelper.WriteLine("Thank you for trying Remotely!  This installer will use your " +
                "GitHub credentials to build a customized Remotely package and install it on this server.");

            ConsoleHelper.WriteLine("You will need to enter a GitHub Personal Access Token, which will " +
                "allow this app to access your fork of the Remotely repo.  You can generate a PAT at " +
                "https://github.com/settings/tokens.  You need to give it the \"repo\" scope.");

            ConsoleHelper.WriteLine("Be sure to retain your GitHub Personal Access Token if you want to re-use it " +
                "for upgrading in the future.  The installer does not save it locally.");


            while (string.IsNullOrWhiteSpace(cliParams.GitHubUsername))
            {
                cliParams.GitHubUsername = ConsoleHelper.ReadLine("Enter your GitHub username").Trim();
            }

            while (string.IsNullOrWhiteSpace(cliParams.GitHubPat))
            {
                cliParams.GitHubPat = ConsoleHelper.ReadLine("Enter your GitHub Personal Access Token").Trim();
            }

            while (string.IsNullOrWhiteSpace(cliParams.Reference))
            {
                ConsoleHelper.WriteLine("Enter the GitHub branch or tag name from which to build.  For example, you can enter " +
                    " \"master\" to build the latest changes from the default branch.  Or you can enter a release tag like \"v2021.04.13.1604\".");
                cliParams.Reference = ConsoleHelper.ReadLine("Input Reference").Trim();
            }

            while (string.IsNullOrWhiteSpace(cliParams.InstallDirectory))
            {
                cliParams.InstallDirectory = ConsoleHelper.ReadLine("Enter the directory path where the server files should be extracted to (e.g. /var/www/remotely/)").Trim();
            }

            while (cliParams.ServerUrl is null)
            {
                var url = ConsoleHelper.ReadLine("Enter your server's public URL (e.g. https://app.remotely.one)").Trim();
                if (Uri.TryCreate(url, UriKind.Absolute, out var serverUrl))
                {
                    cliParams.ServerUrl = serverUrl;
                }

            }

            while (cliParams.CreateNew is null)
            {
                ConsoleHelper.WriteLine("Create new build?  True/false.  If false, the latest existing build artifact on GitHub will be used.");

                var createNew = ConsoleHelper.ReadLine("Selection").Trim();
                if (bool.TryParse(createNew, out var result))
                {
                    cliParams.CreateNew = result;
                }
            }

            while (cliParams.WebServer is null)
            {
                ConsoleHelper.WriteLine("Which reverse proxy will be used?");
                ConsoleHelper.WriteLine("    [0] - Caddy on Ubuntu");
                ConsoleHelper.WriteLine("    [1] - Nginx on Ubuntu");
                ConsoleHelper.WriteLine("    [2] - Caddy on CentOS");
                ConsoleHelper.WriteLine("    [3] - Nginx on CentOS");
                ConsoleHelper.WriteLine("    [4] - IIS on Windows Server 2016+");

                var webServerType = ConsoleHelper.ReadLine("Selection").Trim();
                if (Enum.TryParse<WebServerType>(webServerType, out var result))
                {
                    cliParams.WebServer = result;
                }
            }

            ConsoleHelper.WriteLine($"Performing server install.  GitHub User: {cliParams.GitHubUsername}.  " +
                $"Server URL: {cliParams.ServerUrl}.  Installation Directory: {cliParams.InstallDirectory}");

            var serverInstaller = Services.GetRequiredService<IServerInstaller>();
            await serverInstaller.PerformInstall(cliParams);

            ConsoleHelper.WriteLine("Installation completed.");
        }

        private static void BuildServices()
        {
            var services = new ServiceCollection();

            services.AddSingleton<IGitHubApi, GitHubApi>();
            services.AddSingleton<IServerInstaller, ServerInstaller>();

            if (EnvironmentHelper.IsWindows)
            {
                services.AddSingleton<IElevationDetector, ElevationDetectorWin>();
            }
            else if (EnvironmentHelper.IsLinux)
            {
                services.AddSingleton<IElevationDetector, ElevationDetectorLinux>();
            }
            else
            {
                throw new NotSupportedException("Operating system not supported.");
            }

            Services = services.BuildServiceProvider();
        }

        private static bool ParseCliParams(string[] args, out CliParams cliParams)
        {
            cliParams = new CliParams();

            for (var i = 0; i < args.Length; i += 2)
            {
                try
                {
                    var key = args[i].Trim();
                    var value = args[i + 1].Trim();

                    switch (key)
                    {
                        case "--github-username":
                        case "-u":
                            cliParams.GitHubUsername = value;
                            continue;
                        case "--github-pat":
                        case "-p":
                            cliParams.GitHubPat = value;
                            continue;
                        case "--server-url":
                        case "-s":
                            {
                                if (Uri.TryCreate(value, UriKind.Absolute, out var result))
                                {
                                    cliParams.ServerUrl = result;
                                    continue;
                                }
                                return false;
                            }
                        case "--install-directory":
                        case "-i":
                            cliParams.InstallDirectory = value;
                            continue;
                        case "--reference":
                        case "-r":
                            cliParams.Reference = value;
                            continue;
                        case "--create-new":
                        case "-c":
                            {
                                if (bool.TryParse(value, out var result))
                                {
                                    cliParams.CreateNew = result;
                                }
                                return false;
                            }
                        default:
                            return false;
                    }
                }
                catch (Exception ex)
                {
                    ConsoleHelper.WriteError($"Error while parsing command line arguments: {ex.Message}");
                    return false;
                }

            }
            return true;
        }

        private static void ShowHelpText()
        {
            ConsoleHelper.WriteLine("Remotely Server Installer", 0, ConsoleColor.Cyan);
            ConsoleHelper.WriteLine("Builds a customized Remotely server using GitHub actions " +
                "and installs the server on the local machine.", 1);

            ConsoleHelper.WriteLine("Usage:");

            ConsoleHelper.WriteLine("\tNo Parameters - Run the installer interactively.", 2);

            ConsoleHelper.WriteLine("\t--github-username, -u    Your GitHub username, where the forked Remotely repo exists.", 1);
            
            ConsoleHelper.WriteLine("\t--github-pat, -p    The GitHub Personal Access Token to use for authentication.  " +
                "Create one at ttps://github.com/settings/tokens.", 1);

            ConsoleHelper.WriteLine("\t--server-url, -s    The public URL where your Remotely server will be accessed (e.g. https://app.remotely.one).", 1);

            ConsoleHelper.WriteLine("\t--install-directory, -i    The directory path where the server files will be installed (e.g. /var/www/remotely/).", 1);
            
            ConsoleHelper.WriteLine("Enter the GitHub branch or tag name from which to build.  For example, you can enter " +
                  " \"master\" to build the latest changes from the default branch.  Or you can enter a release tag like \"v2021.04.13.1604\".", 1);
            
            ConsoleHelper.WriteLine("\t--reference, -r    The name of the branch or tag from which to build.  For example, you can enter " +
                  " \"master\" to build the latest changes from the default branch.  Or you can enter a release tag like \"v2021.04.13.1604\".", 1);
            
            ConsoleHelper.WriteLine("\t--create-new, -c    True/false.  Whether to run a new build.  If false, the latest existing build artifact will be used.", 1);
            
            ConsoleHelper.WriteLine("\t--reverse-proxy, -r    Number.  The reverse proxy that will be used to forward requests to the Remotely server.  " +
                "Select the appropriate option for your operating system and web server.  " +
                "0 = Caddy on Ubuntu.  1 = Nginx on Ubuntu.  2 = Caddy on CentOS.  3 = Nginx on CentOS.  4 = IIS on Windows Server 2016+.", 1);
            
            ConsoleHelper.WriteLine("Example: sudo ./Remotely_Server_Installer -u lucent-sea -p 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 -s https://app.remotely.one -i /var/www/remotely/ -r master -c true -r 0");

            ConsoleHelper.ReadLine("Press any key to exit");
        }
    }
}
