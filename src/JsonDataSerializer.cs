using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;

namespace SirenChanger;

// Central JSON helper so the mod stays independent from newer JSON runtime dependencies.
internal static class JsonDataSerializer
{
	// Serialize an object graph using DataContract metadata.
	public static string Serialize<T>(T value)
	{
		DataContractJsonSerializer serializer = CreateSerializer(typeof(T));
		using (MemoryStream stream = new MemoryStream())
		{
			serializer.WriteObject(stream, value);
			return Encoding.UTF8.GetString(stream.ToArray());
		}
	}

	// Parse JSON into a target type and surface a user-facing error instead of throwing.
	public static bool TryDeserialize<T>(string json, out T? value, out string error)
	{
		value = default;
		error = string.Empty;

		if (string.IsNullOrWhiteSpace(json))
		{
			error = "JSON was empty.";
			return false;
		}

		try
		{
			DataContractJsonSerializer serializer = CreateSerializer(typeof(T));
			byte[] bytes = Encoding.UTF8.GetBytes(json);
			using (MemoryStream stream = new MemoryStream(bytes))
			{
				object? parsed = serializer.ReadObject(stream);
				if (parsed is T typed)
				{
					value = typed;
					return true;
				}

				error = $"Unexpected JSON root type: {(parsed == null ? "null" : parsed.GetType().FullName)}.";
				return false;
			}
		}
		catch (Exception ex)
		{
			error = ex.Message;
			return false;
		}
	}

	// Use simple dictionary format so settings stay readable and stable.
	private static DataContractJsonSerializer CreateSerializer(Type type)
	{
		return new DataContractJsonSerializer(type, new DataContractJsonSerializerSettings
		{
			UseSimpleDictionaryFormat = true
		});
	}
}
