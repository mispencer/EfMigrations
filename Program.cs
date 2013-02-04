using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using System.Xml.Linq;
using System.Linq;
using System.Text;
using System.Resources;
using System.Configuration;
using System.Web.Configuration;
using System.Reflection;
using System.Data.Entity.Migrations;
using System.Data.Entity.Infrastructure;
using System.Data.Entity.Migrations.Design;

namespace SpencerMigration {
	public class Program {
		public static int Main(String[] argsArray) {
			var args = new Queue<String>(argsArray);
			String baseFolder = null;
			String migrationName = null;
			
			while(args.Count > 0) {
				var currentArg = args.Peek();
				if (currentArg == "-n" || currentArg == "--name") {
					args.Dequeue();
					migrationName = args.Dequeue();
				} else if (currentArg == "-b" || currentArg == "--basefolder") {
					args.Dequeue();
					migrationName = args.Dequeue();
				} else {
					break;
				}
			}
			if (args.Count > 0 && migrationName == null) {
				migrationName = args.Dequeue();
			}
			if (args.Count > 0 && baseFolder == null) {
				baseFolder = args.Dequeue();
			}
			if (baseFolder == null) {
				baseFolder = Directory.GetCurrentDirectory();
				while(true) {
					var di = new DirectoryInfo(baseFolder);
					var hasProjectFile = di.GetFiles("*.csproj").Any();
					if (hasProjectFile) break;
					if (di.Parent == null) throw new Exception("Could not find project folder");
					baseFolder = di.Parent.FullName;
				}
			}

			var migrationCreator = new MigrationCreator(baseFolder);
			migrationCreator.AddMigration(migrationName);
			return 0;
		}
	}

	public class MigrationCreator {
		public const String PROJECT_NAMESPACE = "http://schemas.microsoft.com/developer/msbuild/2003";

		private readonly String _baseFolder;

		public MigrationCreator(String baseFolder) {
			_baseFolder = baseFolder;
		}

		public void AddMigration(String migrationName) {

			//Console.WriteLine(_baseFolder);
			var projectPath = GetProjectPath();
			//Console.WriteLine("ProjectPath: {0}", projectPath);
			var projectXml = XElement.Load(projectPath);

			var dataConfig = GetDbMigrationConfig(projectXml);
			UpdateAppConfig();

			var scaffolder = new MigrationScaffolder(dataConfig);
			var scaffold = scaffolder.Scaffold(migrationName, false);

			//Console.WriteLine("scaffold.UserCode");
			//Console.WriteLine(scaffold.UserCode);
			//Console.WriteLine("scaffold.DesignerCode");
			//Console.WriteLine(scaffold.DesignerCode);
			//Console.WriteLine("scaffold.Directory");
			//Console.WriteLine(scaffold.Directory);
			//Console.WriteLine("scaffold.Resources");
			//Console.WriteLine(String.Join(",", scaffold.Resources.OrderBy(kv => kv.Key)));
			//Console.WriteLine("scaffold.Language");
			//Console.WriteLine(scaffold.Language);
			//Console.WriteLine("scaffold.MigrationId");
			//Console.WriteLine(scaffold.MigrationId);

			SaveMigration(projectXml, scaffold);

			projectXml.Save(projectPath, SaveOptions.OmitDuplicateNamespaces);
		}

		private String GetProjectPath() {
			var di = new DirectoryInfo(_baseFolder);
			var projectFile = di.GetFiles("*.csproj").First();
			return projectFile.FullName;
		}

		private DbMigrationsConfiguration GetDbMigrationConfig(XElement projectXml) {
			var assemblyName = projectXml.Descendants(XName.Get("AssemblyName", PROJECT_NAMESPACE)).Select(i => i.Value+".dll").First();
			var outputPath = projectXml.Descendants(XName.Get("OutputPath", PROJECT_NAMESPACE)).Select(i => i.Value).First();
			var assemblyPath = Path.Combine(_baseFolder,outputPath,assemblyName);
			//Console.WriteLine("AssemblyPath: {0}", assemblyPath);
			var assembly = Assembly.LoadFrom(assemblyPath);
			var dataConfigType = assembly.GetTypes().Where(i => typeof(DbMigrationsConfiguration).IsAssignableFrom(i)).First();
			return (DbMigrationsConfiguration) Activator.CreateInstance(dataConfigType);
		}

		private void UpdateAppConfig() {
			var mapping = new WebConfigurationFileMap();
			mapping.VirtualDirectories.Add("", new VirtualDirectoryMapping(_baseFolder, true, "web.Config"));
			var webConfig = WebConfigurationManager.OpenMappedWebConfiguration(mapping, "");
			var appConfig = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
			foreach (var connectionString in webConfig.ConnectionStrings.ConnectionStrings) {
				appConfig.ConnectionStrings.ConnectionStrings.Add((ConnectionStringSettings)connectionString);
			}
			appConfig.Save();
			ConfigurationManager.RefreshSection("connectionStrings");
		}

		private void SaveMigration(XElement projectXml, ScaffoldedMigration scaffold) {
			var itemGroupName = XName.Get("ItemGroup", PROJECT_NAMESPACE);
			var lastItemGroup = projectXml.Elements(itemGroupName).Last();
			var newItemGroup = new XElement(itemGroupName);

			var migrationUserFileName = String.Format("{0}.{1}", scaffold.MigrationId, scaffold.Language);
			var migrationUserPath = Path.Combine(scaffold.Directory, migrationUserFileName);
			File.WriteAllText(Path.Combine(_baseFolder, migrationUserPath), scaffold.UserCode);
			var migrationUserItem = new XElement(XName.Get("Compile", PROJECT_NAMESPACE));
			migrationUserItem.SetAttributeValue(XName.Get("Include"), migrationUserPath);
			newItemGroup.Add(migrationUserItem);

			var migrationDesignerPath = Path.Combine(scaffold.Directory, String.Format("{0}.Designer.{1}", scaffold.MigrationId, scaffold.Language));
			File.WriteAllText(Path.Combine(_baseFolder, migrationDesignerPath), scaffold.DesignerCode);
			var migrationDesignerItem = new XElement(XName.Get("Compile", PROJECT_NAMESPACE));
			migrationDesignerItem.SetAttributeValue(XName.Get("Include"), migrationDesignerPath);
			var migrationDesignerDependentUponItem = new XElement(XName.Get("DependentUpon", PROJECT_NAMESPACE));
			migrationDesignerDependentUponItem.Value = migrationUserFileName;
			migrationDesignerItem.Add(migrationDesignerDependentUponItem);
			newItemGroup.Add(migrationDesignerItem);

			var migrationResourcePath = Path.Combine(scaffold.Directory, String.Format("{0}.resx", scaffold.MigrationId));
			var writer = new ResXResourceWriter(Path.Combine(_baseFolder, migrationResourcePath));
			foreach(var resource in scaffold.Resources) {
				writer.AddResource(resource.Key, resource.Value);
			}
			writer.Close();
			var migrationResourceItem = new XElement(XName.Get("EmbeddedResource", PROJECT_NAMESPACE));
			migrationResourceItem.SetAttributeValue(XName.Get("Include"), migrationResourcePath);
			var migrationResourceDependentUponItem = new XElement(XName.Get("DependentUpon", PROJECT_NAMESPACE));
			migrationResourceDependentUponItem.Value = migrationUserFileName;
			migrationResourceItem.Add(migrationResourceDependentUponItem);
			newItemGroup.Add(migrationResourceItem);

			lastItemGroup.AddAfterSelf(newItemGroup);
		}
	}
}
