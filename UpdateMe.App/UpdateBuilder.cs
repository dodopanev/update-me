﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NDesk.Options;
using System.IO;
using NuGet;
using System.Diagnostics;

namespace UpdateMe.App
{
    public class UpdateBuilder
    {
        private const string DIRECTORY_BASE = "/lib/net45";
        private const string SQUIRREL_PATH = @"tools\Squirrel.exe";
        private readonly string[] FILES_EXCLUDE_LIST = { ".pdb", ".vshost" };

        private IDistributor _distributor;
        public UpdateBuilder(IDistributor distributor)
        {
            if (distributor == null)
            {
                throw new ArgumentNullException("distributor cannot be null");
            }

            _distributor = distributor;
        }

        public void ReleaseNewVersion(RelaseRequest request)
        {
            if (request == null || request.Validate() == false)
            {
                return;
            }

            CreateNugetPackage(request);

            if (SquirrelReleasify(request))
            {
                DistributeFiles(request);

                $"Application version {request.Version} has been releasead".WriteSuccessToConsole();
            }

        }

        private void CreateNugetPackage(RelaseRequest request)
        {
            var metadata = new ManifestMetadata()
            {
                Id = request.AppId,
                Authors = request.Authors,
                Version = request.Version,
                Description = request.Title,
                Title = request.Title,
                IconUrl = request.AppIcon
            };

            PackageBuilder builder = new PackageBuilder();
            builder.Populate(metadata);

            //As Squirrel convention i put everything in lib/net45 folder
            List<ManifestFile> files = new List<ManifestFile>();

            AddDirectoryFilesToPath(request.SourceDir, files, true);

            builder.PopulateFiles("", files.ToArray());

            if (Directory.Exists(Path.GetDirectoryName(request.NugetPackagePath)) == false)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(request.NugetPackagePath));
            }

            if (File.Exists(request.NugetPackagePath))
            {
                File.Delete(request.NugetPackagePath);
            }

            using (FileStream stream = File.Open(request.NugetPackagePath, FileMode.OpenOrCreate))
            {
                builder.Save(stream);
            }

            $"Nuget package {request.AppId}.{request.Version}.nupkg created".WriteSuccessToConsole();
        }

        private bool SquirrelReleasify(RelaseRequest request)
        {
            "Creating release packages...".WriteInfoToConsole();
            bool isSuccess = false;
            StringBuilder cmd = new StringBuilder();

            cmd.AppendFormat("-releasify \"{0}\"  -releaseDir \"{1}\"", request.NugetPackagePath, request.ReleasePath);
            if (File.Exists(request.AppIcon))
            {
                cmd.AppendFormat(" -setupIcon \"{0}\"", request.AppIcon);
            }

            if (File.Exists(request.SignCertificatePath))
            {
                cmd.AppendFormat(" -n \"/t http://timestamp.comodoca.com/authenticode /f \"{0}\" /p {1}\" ", request.SignCertificatePath, request.SignCertificatePassword);
            }

            ProcessStartInfo startInfo = new ProcessStartInfo(SQUIRREL_PATH, cmd.ToString())
            {
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using (Process exeProcess = Process.Start(startInfo))
            {
                exeProcess.WaitForExit();
                if (exeProcess.ExitCode == -1)
                {
                    "Squirrel failed please check error log".WriteErrorToConsole();
                    isSuccess = false;
                }
                else
                {
                    "Squirrel installation files created".WriteSuccessToConsole();
                    isSuccess = true;
                }

            }
            return isSuccess;
        }

        private void DistributeFiles(RelaseRequest request)
        {
            List<string> filesToDistribute = new List<string>
            {
                Path.Combine(request.ReleasePath,"RELEASES"),
                Path.Combine(request.ReleasePath,"Setup.exe"),
                Path.Combine(request.ReleasePath,"Setup.msi"),
                Path.Combine(request.ReleasePath,$"{request.AppId}-{request.Version}-full.nupkg"),
            };
            string deltaFile = Path.Combine(request.ReleasePath, $"{request.AppId}-{request.Version}-delta.nupkg");
            if (File.Exists(deltaFile))
            {
                filesToDistribute.Add(deltaFile);
            }
            filesToDistribute.Reverse();
            _distributor.DistributeRelease(filesToDistribute);
        }

        private void AddDirectoryFilesToPath(string directory, List<ManifestFile> files, bool isRoot)
        {
            foreach (string directoryPath in Directory.GetDirectories(directory))
            {
                AddDirectoryFilesToPath(directoryPath, files, false);
            }

            foreach (string filePath in Directory.GetFiles(directory).Where(c => FILES_EXCLUDE_LIST.Contains(Path.GetExtension(c)) == false))
            {
                files.Add(new ManifestFile()
                {
                    Source = filePath,
                    Target = isRoot ? Path.Combine(DIRECTORY_BASE, Path.GetFileName(filePath)) :
                                                     Path.Combine(DIRECTORY_BASE, Path.GetFileName(directory), Path.GetFileName(filePath))
                });
            }
        }

        private void ConsoleWriteError(string message)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(message);
            Console.ForegroundColor = ConsoleColor.Gray;
        }
    }


}