using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Obsidian.API;
using Obsidian.Utilities.Converters;
using Obsidian.Utilities.Registry.Enums;

namespace Obsidian.Utilities
{
    public static partial class Extensions
    {
        public static IServiceCollection AddJsonSerialization(this IServiceCollection services)
        {
            services.Configure<JsonSerializerOptions>(options =>
            {
                var snakeCasePolicy = new SnakeCaseNamingPolicy();

                options.PropertyNamingPolicy = snakeCasePolicy;
                options.DictionaryKeyPolicy = snakeCasePolicy;

                options
                    .AddStringEnumConverter<CustomDirection>()
                    .AddStringEnumConverter<Axis>()
                    .AddStringEnumConverter<Face>()
                    .AddStringEnumConverter<BlockFace>()
                    .AddStringEnumConverter<EHalf>()
                    .AddStringEnumConverter<Hinge>()
                    .AddStringEnumConverter<Instruments>()
                    .AddStringEnumConverter<Part>()
                    .AddStringEnumConverter<Shape>()
                    .AddStringEnumConverter<MinecraftType>()
                    .AddStringEnumConverter<Attachment>();
            });

            return services;
        }
    }
}
