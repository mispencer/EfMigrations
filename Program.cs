using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using System.Linq;
using System.Text;
#if (EF5 || EF6)
using System.Resources;
#endif
using System.Data.Entity.Infrastructure;
using System.Data.Entity.Migrations.Design;

namespace EfMigrations {
	public class Program {
		public static int Main(String[] argsArray) {
			var args = new Queue<String>(argsArray);
			String action = null;
			String baseFolder = null;
			String binFolder = null;
			String assemblyName = null;
			String configPath = null;
			String migrationName = null;
			String sourceMigration = null;
			String targetMigration = null;
			String connectionName = null;
			
			while(args.Count > 0) {
				var currentArg = args.Peek();
				if (currentArg == "-a" || currentArg == "--action") {
					args.Dequeue();
					action = args.Dequeue();
				} else if (currentArg == "-n" || currentArg == "--name") {
					args.Dequeue();
					migrationName = args.Dequeue();
				} else if (currentArg == "--sourceMigration") {
					args.Dequeue();
					sourceMigration = args.Dequeue();
				} else if (currentArg == "--targetMigration") {
					args.Dequeue();
					targetMigration = args.Dequeue();
				} else if (currentArg == "-b" || currentArg == "--baseFolder") {
					args.Dequeue();
					baseFolder = args.Dequeue();
				} else if (currentArg == "--binFolder") {
					args.Dequeue();
					binFolder = args.Dequeue();
				} else if (currentArg == "--assemblyName") {
					args.Dequeue();
					assemblyName = args.Dequeue();
				} else if (currentArg == "--configPath") {
					args.Dequeue();
					configPath = args.Dequeue();
				} else if (currentArg == "--connectionName") {
					args.Dequeue();
					connectionName = args.Dequeue();
				} else {
					break;
				}
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

			var migrationFacade = new MigrationFacade(baseFolder, assemblyName, binFolder, configPath, connectionName);
			if (action != null) {
				switch (action) {
					case "AddMigration":
						migrationFacade.AddMigration(migrationName);
						break;
					case "PendingMigrations":
						migrationFacade.ListPending();
						break;
					case "UpdateDatabase":
						migrationFacade.UpdateDatabase(targetMigration);
						break;
					case "GenerateScript":
						migrationFacade.GenerateScript(sourceMigration, targetMigration);
						break;
					default:
						throw new Exception("Action: "+action);
				}
			}
			return 0;
		}
	}

	public class MigrationFacade {
		private const String PROJECT_NAMESPACE = "http://schemas.microsoft.com/developer/msbuild/2003";

		private readonly String _baseFolder;
		private readonly String _projectPath;
		private readonly ToolingFacade _fasade;

		public MigrationFacade(String baseFolder, String assemblyName, String binFolder, String configPath, String connectionName) {
			_baseFolder = baseFolder;
			if (String.IsNullOrWhiteSpace(binFolder) || String.IsNullOrWhiteSpace(assemblyName)) {
				_projectPath = (new DirectoryInfo(_baseFolder)).GetFiles("*.csproj").First().FullName;

				var projectXml = XElement.Load(_projectPath);
				if (String.IsNullOrWhiteSpace(assemblyName)) {
					assemblyName = projectXml.Descendants(XName.Get("AssemblyName", PROJECT_NAMESPACE)).Select(i => i.Value).First();
				}
				if (String.IsNullOrWhiteSpace(binFolder)) {
					var outputPath = projectXml.Descendants(XName.Get("OutputPath", PROJECT_NAMESPACE)).Select(i => i.Value).First();
					binFolder = Path.Combine(_baseFolder, outputPath);
				}
			}
			if (String.IsNullOrWhiteSpace(configPath)) {
				configPath = new DirectoryInfo(_baseFolder).GetFiles("Web.config").Any() ? Path.Combine(_baseFolder, "Web.config") : Path.Combine(_baseFolder, "app.config");
			}

#if EF6
			_fasade = new ToolingFacade(assemblyName, assemblyName, null, binFolder, configPath, null, String.IsNullOrWhiteSpace(connectionName) ? null : new DbConnectionInfo(connectionName));
#else
			_fasade = new ToolingFacade(assemblyName, null, binFolder, configPath, null, String.IsNullOrWhiteSpace(connectionName) ? null : new DbConnectionInfo(connectionName));
#endif
		}

		public void UpdateDatabase(String targetMigration) {
			_fasade.Update(targetMigration, false);
		}

		public void ListPending() {
			var pending = _fasade.GetPendingMigrations();
			Console.WriteLine("Pending count: "+pending.Count());
			foreach(var pend in pending) {
				Console.WriteLine("\t"+pend);
			}
		}

		public void GenerateScript(String sourceMigration, String targetMigration) {
			var script = _fasade.ScriptUpdate(sourceMigration, targetMigration, false);
			Console.Write(script);
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
