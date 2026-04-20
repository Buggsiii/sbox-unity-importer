using System.Collections.Generic;
using System.IO;
using System.Linq;
using Editor;
using Sandbox;
using static Bugge.UnityImporter.UnityPackageExtractor;

namespace Bugge.UnityImporter;

public static class UnityPrefabConverter
{
	public struct UnityObject
	{
		public string Parent;
		public Dictionary<string, string[]> Components;
	}

	public static void ConvertPrefab( UnityFile[] files )
	{
		UnityFile[] prefabs = [.. files.Where( f => f.Included && Path.GetExtension( f.Path ) == ".prefab" )];
		Log.Info( "Converting " + prefabs.Length + " materials..." );
	}

	public static UnityObject[] ParseUnityPrefab( UnityFile prefab )
	{
		var objects = new List<UnityObject>();
		return [.. objects];
	}

	public static Asset CreatePrefab( GameObject root, string prefabFilename )
	{
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

		var options = new System.Text.Json.JsonSerializerOptions
		{
			WriteIndented = true,
			TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver()
		};

		string pretty = prefabJson.ToJsonString( options );
		File.WriteAllText( prefabFilename, pretty );
		var prefabAsset = AssetSystem.RegisterFile( prefabFilename );
		prefabAsset?.Compile( true );

		return prefabAsset;
	}
}
