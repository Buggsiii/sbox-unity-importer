using System;
using System.Collections.Generic;
using System.Linq;
using Editor;
using Sandbox;

public class SplitMeshContextMenu
{
	private static readonly HashSet<string> MeshExtensions = new( StringComparer.OrdinalIgnoreCase )
	{
		"fbx",
		"obj",
		"dmx"
	};

	[Event( "asset.contextmenu", Priority = 50 )]
	private protected static void OnMeshFileAssetContext( AssetContextMenu e )
	{
		var meshes = e.SelectedList
			.Where( x => x.Asset is not null && MeshExtensions.Contains( x.AssetType.FileExtension ) )
			.Select( x => x.Asset )
			.ToList();

		if ( meshes.Count != 0 )
		{
			if ( meshes.Count == 1 )
			{
				var mdl = meshes.First();
				e.Menu.AddOption( "Split Mesh..", "open_in_new", (System.Action)(() =>
				{
					var targetPath = EditorUtility.SaveFileDialog( "Create Prefab..", "prefab", System.IO.Path.ChangeExtension( mdl.AbsolutePath, "prefab" ) );
					if ( targetPath is null )
						return;

					SplitMesh( mdl, targetPath );
				}) );
			}
			else
			{
				e.Menu.AddOption( $"Split into {meshes.Count} meshes", "open_in_new", () => meshes.ForEach( asset => SplitMesh( asset ) ) );
			}
		}
	}

	public static Asset SplitMesh( Asset meshFile, string targetAbsolutePath = null )
	{
		var sourceFile = meshFile.GetSourceFile( true );
		var folderPath = System.IO.Path.Combine( System.IO.Path.GetDirectoryName( sourceFile ) ?? string.Empty, System.IO.Path.GetFileNameWithoutExtension( sourceFile ) );

		var partNames = GetMeshPartsFromMeshFile( meshFile );
		if ( partNames.Length == 0 )
			return EditorUtility.CreateModelFromMeshFile( meshFile, null );

		if ( !System.IO.Directory.Exists( folderPath ) )
			System.IO.Directory.CreateDirectory( folderPath );

		var partModels = new List<(string name, string path)>();
		foreach ( var part in partNames )
		{
			var safe = MakeSafeFilename( part );
			var partVmdl = System.IO.Path.Combine( folderPath, $"{System.IO.Path.GetFileNameWithoutExtension( sourceFile )}_{safe}.vmdl" );
			var partAsset = CreateModelFromMeshFile( meshFile, partVmdl, [part], excludeByDefault: true );
			if ( partAsset != null )
				partModels.Add( (name: part, path: partAsset.Path ?? partVmdl) );
		}

		var prefabFilename = targetAbsolutePath ?? System.IO.Path.ChangeExtension( sourceFile, ".prefab" );

		// Preserve guids from existing prefab so scene instances stay linked
		string existingRootGuid = null;
		var existingChildGuids = new Dictionary<string, string>();
		if ( System.IO.File.Exists( prefabFilename ) )
		{
			try
			{
				var existing = System.Text.Json.Nodes.JsonNode.Parse( System.IO.File.ReadAllText( prefabFilename ) );
				existingRootGuid = existing?["RootObject"]?["__guid"]?.GetValue<string>();
				var children = existing?["RootObject"]?["Children"]?.AsArray();
				if ( children != null )
					foreach ( var child in children )
					{
						var name = child?["Name"]?.GetValue<string>();
						var guid = child?["__guid"]?.GetValue<string>();
						if ( name != null && guid != null )
							existingChildGuids[name] = guid;
					}
			}
			catch { }
		}

		var prefabScene = new Scene();

		using ( prefabScene.Push() )
		{
			var root = new GameObject
			{
				Name = System.IO.Path.GetFileNameWithoutExtension( prefabFilename )
			};

			foreach ( var (name, path) in partModels )
			{
				var partObj = new GameObject
				{
					Name = name,
					Parent = root
				};

				var renderer = partObj.Components.Create<ModelRenderer>();
				renderer.Model = Model.Load( path );
			}

			var rootJson = root.Serialize();

			if ( existingRootGuid != null )
				rootJson["__guid"] = existingRootGuid;

			var childrenJson = rootJson["Children"]?.AsArray();
			if ( childrenJson != null )
				foreach ( var child in childrenJson )
				{
					var name = child?["Name"]?.GetValue<string>();
					if ( name != null && existingChildGuids.TryGetValue( name, out var guid ) )
						child["__guid"] = guid;
				}

			var prefabJson = new System.Text.Json.Nodes.JsonObject
			{
				["RootObject"] = rootJson,
				["ResourceVersion"] = 2,
				["ShowInMenu"] = false,
				["MenuPath"] = null,
				["MenuIcon"] = null,
				["DontBreakAsTemplate"] = false,
				["__references"] = new System.Text.Json.Nodes.JsonArray(),
				["__version"] = 2
			};

			prefabScene.Destroy();

			System.IO.File.WriteAllText( prefabFilename, prefabJson.ToJsonString() );
			var prefabAsset = AssetSystem.RegisterFile( prefabFilename );
			prefabAsset?.Compile( true );

			return prefabAsset;
		}
	}

	public static Asset CreateModelFromMeshFile( Asset meshFile, string targetAbsolutePath = null, IEnumerable<string> excludedParts = null, bool excludeByDefault = false )
	{
		var modelFilename = targetAbsolutePath ?? System.IO.Path.ChangeExtension( meshFile.GetSourceFile( true ), ".vmdl" );

		if ( System.IO.File.Exists( modelFilename ) )
			System.IO.File.Delete( modelFilename );

		string modelCompiledPath = modelFilename + "_c"; // .vmdl_c
		if ( System.IO.File.Exists( modelCompiledPath ) )
			System.IO.File.Delete( modelCompiledPath );

		var asset = EditorUtility.CreateModelFromMeshFile( meshFile, modelFilename );

		if ( asset is null )
		{
			Log.Warning( $"Asset is null! modelFilename={modelFilename}" );
			return null;
		}

		bool shouldApplyFilter = excludedParts is not null && excludedParts.Any();
		if ( shouldApplyFilter )
			AddImportFilterExceptionList( modelFilename, excludedParts, excludeByDefault );

		asset.Compile( true );

		return asset;
	}

	private static void AddImportFilterExceptionList( string vmdlPath, IEnumerable<string> exceptionList, bool excludeByDefault = false )
	{
		string text = System.IO.File.ReadAllText( vmdlPath );
		string marker = "import_filter";
		string insertBlock = $"import_filter =\r\n{{\r\n    exclude_by_default = {excludeByDefault.ToString().ToLowerInvariant()}\r\n    exception_list =\r\n    [\r\n{string.Join( "\r\n", exceptionList.Select( n => "        \"" + n.Replace( "\"", "\\\"" ) + "\"" ) )}\r\n    ]\r\n}}\r\n";

		if ( text.Contains( marker, StringComparison.OrdinalIgnoreCase ) )
		{
			// replace existing import_filter block in RenderMeshFile if present (best-effort)
			int start = text.IndexOf( marker, StringComparison.OrdinalIgnoreCase );
			int brace = text.IndexOf( '{', start );
			if ( brace >= 0 )
			{
				int level = 0;
				int end = -1;
				for ( int i = brace; i < text.Length; i++ )
				{
					if ( text[i] == '{' ) level++;
					else if ( text[i] == '}' ) level--;
					if ( level == 0 )
					{
						end = i;
						break;
					}
				}

				if ( end >= 0 )
				{
					text = string.Concat( text.AsSpan( 0, start ), insertBlock, text.AsSpan( end + 1 ) );
				}
				else
				{
					text += "\r\n" + insertBlock;
				}
			}
			else
			{
				text += "\r\n" + insertBlock;
			}
		}
		else
		{
			text += "\r\n" + insertBlock;
		}

		System.IO.File.WriteAllText( vmdlPath, text, System.Text.Encoding.UTF8 );
	}

	public static string[] GetMeshPartsFromMeshFile( Asset meshFile, string targetAbsolutePath = null )
	{
		string sourceFile = meshFile.GetSourceFile( true );
		string modelFilename = targetAbsolutePath ?? System.IO.Path.ChangeExtension( sourceFile, ".vmdl" );

		bool didExistBefore = System.IO.File.Exists( modelFilename );
		if ( !didExistBefore )
			_ = EditorUtility.CreateModelFromMeshFile( meshFile, modelFilename );

		if ( !System.IO.File.Exists( modelFilename ) )
		{
			Log.Warning( "File does not exist!" );
			return [];
		}


		try
		{
			var partsFromVmdl = ParseImportFilterExceptionList( modelFilename );
			return partsFromVmdl.Length > 0 ? partsFromVmdl : [];
		}
		finally
		{
			if ( !didExistBefore )
			{
				if ( System.IO.File.Exists( modelFilename ) )
					System.IO.File.Delete( modelFilename );

				string compiledPath = modelFilename + "_c"; // .vmdl_c
				if ( System.IO.File.Exists( compiledPath ) )
					System.IO.File.Delete( compiledPath );
			}
		}
	}

	private static string[] ParseImportFilterExceptionList( string vmdlPath )
	{
		if ( !System.IO.File.Exists( vmdlPath ) ) return [];

		var text = System.IO.File.ReadAllText( vmdlPath );
		var marker = "exception_list";
		var start = text.IndexOf( marker, StringComparison.OrdinalIgnoreCase );
		if ( start < 0 ) return [];

		var bracketStart = text.IndexOf( '[', start );
		if ( bracketStart < 0 ) return [];

		var bracketEnd = text.IndexOf( ']', bracketStart );
		if ( bracketEnd < 0 ) return [];

		var block = text[(bracketStart + 1)..bracketEnd];

		var entries = block
			.Split( ['\r', '\n'], StringSplitOptions.RemoveEmptyEntries )
			.Select( line => line.Trim() )
			.Select( line =>
			{
				line = line.TrimEnd( ',' ).Trim();
				if ( line.StartsWith( '"' ) && line.EndsWith( '"' ) && line.Length >= 2 )
					line = line[1..^1];
				return line.Replace( "\\\"", "\"" ).Trim();
			} )
			.Where( line => !string.IsNullOrWhiteSpace( line ) )
			.Distinct( StringComparer.OrdinalIgnoreCase )
			.ToArray();

		return entries;
	}

	private static string MakeSafeFilename( string name )
	{
		if ( string.IsNullOrWhiteSpace( name ) )
			return "mesh";

		var invalidChars = System.IO.Path.GetInvalidFileNameChars();
		var safe = new string( [.. name.Select( c => invalidChars.Contains( c ) ? '_' : c )] );

		return string.IsNullOrWhiteSpace( safe ) ? "mesh" : safe;
	}
}
