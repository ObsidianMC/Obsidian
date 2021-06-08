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
            services.Configure<JsonSerializerSettings>(options =>
            {
                var snakeCaseStrategy = new SnakeCaseNamingStrategy();

                options.ContractResolver = new DefaultContractResolver() { NamingStrategy = snakeCaseStrategy };

                options.Converters.Add(new DefaultEnumConverter<CustomDirection>());
                options.Converters.Add(new DefaultEnumConverter<Axis>());
                options.Converters.Add(new DefaultEnumConverter<Face>());
                options.Converters.Add(new DefaultEnumConverter<BlockFace>());
                options.Converters.Add(new DefaultEnumConverter<EHalf>());
                options.Converters.Add(new DefaultEnumConverter<Hinge>());
                options.Converters.Add(new DefaultEnumConverter<Instruments>());
                options.Converters.Add(new DefaultEnumConverter<Part>());
                options.Converters.Add(new DefaultEnumConverter<Shape>());
                options.Converters.Add(new DefaultEnumConverter<MinecraftType>());
                options.Converters.Add(new DefaultEnumConverter<Attachment>());
            });

            return services;
        }
    }
}
