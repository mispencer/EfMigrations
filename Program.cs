using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using System.Linq;
using System.Text;
#if (EF5 || EF6)
using System.Resources;
#endif
using System.Data.Entity.Migrations.Design;

namespace EfMigrations {
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
					baseFolder = args.Dequeue();
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
		private const String PROJECT_NAMESPACE = "http://schemas.microsoft.com/developer/msbuild/2003";

		private readonly String _baseFolder;
		private readonly String _projectPath;
		private readonly ToolingFacade _fasade;

		public MigrationCreator(String baseFolder) {
			_baseFolder = baseFolder;
			_projectPath = (new DirectoryInfo(_baseFolder)).GetFiles("*.csproj").First().FullName;

			var projectXml = XElement.Load(_projectPath);
			var assemblyName = projectXml.Descendants(XName.Get("AssemblyName", PROJECT_NAMESPACE)).Select(i => i.Value).First();
			var outputPath = projectXml.Descendants(XName.Get("OutputPath", PROJECT_NAMESPACE)).Select(i => i.Value).First();
			var binFolder = Path.Combine(_baseFolder, outputPath);
			var configPath = new DirectoryInfo(_baseFolder).GetFiles("Web.config").Any() ? Path.Combine(_baseFolder, "Web.config") : Path.Combine(_baseFolder, "App.config");

#if EF6
			_fasade = new ToolingFacade(assemblyName, assemblyName, null, binFolder, configPath, null, null);
#else
			_fasade = new ToolingFacade(assemblyName, null, binFolder, configPath, null, null);
#endif
		}

		public void AddMigration(String migrationName) {
			SaveMigration(_fasade.Scaffold(migrationName, null, null, false));
		}

		private void SaveMigration(ScaffoldedMigration scaffold) {

			var itemGroupName = XName.Get("ItemGroup", PROJECT_NAMESPACE);
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
			var migrationDesignerDependentUponItem = new XElement(XName.Get("DependentUpon", PROJECT_NAMESPACE)) {
				Value = migrationUserFileName
			};
		    migrationDesignerItem.Add(migrationDesignerDependentUponItem);
			newItemGroup.Add(migrationDesignerItem);

#if (EF5 || EF6)
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
#endif

			var projectXml = XElement.Load(_projectPath);
			var lastItemGroup = projectXml.Elements(itemGroupName).Last();
			lastItemGroup.AddAfterSelf(newItemGroup);
			projectXml.Save(_projectPath, SaveOptions.OmitDuplicateNamespaces);
		}
	}
}
