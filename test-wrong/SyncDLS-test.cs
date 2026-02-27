using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using sDIR.Domains.REP.Database;
using sDIR.Domains.REP.Database.Model.DLS;
using sDIR.Libraries.Base.Worker.Tasks;
using sDIR.Libraries.Common.Interpreters.CSV;
using sDIR.Libraries.Common.Jobs.Attributes;
using sDIR.Libraries.Common.Storage.Common;
using sDIR.Libraries.Common.Storage.Protocols.GoogleCloud;
using sDIR.Libraries.Common.Storage.Protocols.Shared;
using System.Diagnostics;

namespace sDIR.Services.REP.Worker.Jobs.DLS
{
#if DEBUG
    [WithCron("0 0/5 * * * ?", AllowConcurrentExecution = false, FireOnceOnStartup = true)]
#else
    [WithCron("0 30 3 * * ?", AllowConcurrentExecution = false, FireOnceOnStartup = true)]
#endif
    [WithIdentity(nameof(SyncDLS))]
    public sealed class SyncDLS
         : BackgroundTaskBase
    {
        #region Private variable declarations.

        private readonly ILogger<SyncDLS> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly IGoogleCloudClient _googleCloudClient;
        private readonly Stopwatch _stopwatch = new Stopwatch();
        private readonly string[] FILE_DIRECTORY = ["DLS", "Receive"];

        #endregion

        #region Constant variable declarations.

        private const string C_DLS_IPRANGE_FILENANME = "dls_ip_range_spk.txt";
        private const string C_DLS_IPClIENT_FILENANME = "dls_pp_ipclient_spk.txt";
        private const string C_DLS_PORT_FILENANME = "dls_port_spk.txt";
        private const string C_DLS_MOBILITY_FILENANME = "mobility_spk.txt";

        #endregion

        /// <summary>
        /// The <see cref="SyncDLS"/> is used to sync the data from the <see cref="IGoogleCloudClient"/> into the <see cref="RepDataContext"/>.
        /// This data is needed for for device Infos
        /// </summary>
        /// <param name="logger"><see cref="ILogger"/> to use.</param>
        /// <param name="serviceProvider"><see cref="IServiceProvider"/> to access services.</param>
        public SyncDLS(
            ILogger<SyncDLS> logger,
            IGoogleCloudClient googleCloudClient,
            IServiceProvider serviceProvider
        )
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _googleCloudClient = googleCloudClient;
        }

        /// <inheritdoc />
        protected override async Task DoWorkAsync(CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation($"Starting execution of {nameof(SyncDLS)} task.");
                _stopwatch.Restart();
                using (IServiceScope serviceScope = _serviceProvider.CreateScope())
                using (RepDataContext repDataContext = serviceScope.ServiceProvider.GetRequiredService<RepDataContext>())
                {
                    await GetAndSave(repDataContext, cancellationToken);
                }
                _stopwatch.Stop();
                _logger.LogInformation($"{(cancellationToken.IsCancellationRequested ? "Aborted" : "Finished")} execution of {nameof(SyncDLS)} task after {_stopwatch.Elapsed.ToString(@"hh\:mm\:ss")}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error Sync Network Report");
                throw;
            }
        }

        /// <summary>
        /// Asynchronously retrieves a the dls data, adapts the data, deletes old data, and saves it to the database.
        /// </summary>
        /// <param name="repDataContext">Database context to save the adapted data.</param>
        /// <param name="cancellationToken"><see cref="CancellationToken"/> to monitor for cancellation requests.</param>
        public async Task GetAndSave(
            RepDataContext repDataContext,
            CancellationToken cancellationToken
        )
        {
            #region Load data from GoogleCloud and Import CSV Files

            List<DlsIpRange> ipRanges = new List<DlsIpRange>();
            List<DlsIpClient> ipClients = new List<DlsIpClient>();
            List<DlsPort> ports = new List<DlsPort>();
            List<DlsMobility> mobilities = new List<DlsMobility>();

            StorageItem ipRangeItem = new StorageItem()
            {
                AbstractLocation = FILE_DIRECTORY,
                FileName = C_DLS_IPRANGE_FILENANME
            };
            StorageItem ipClientItem = new StorageItem()
            {
                AbstractLocation = FILE_DIRECTORY,
                FileName = C_DLS_IPClIENT_FILENANME
            };
            StorageItem portItem = new StorageItem()
            {
                AbstractLocation = FILE_DIRECTORY,
                FileName = C_DLS_PORT_FILENANME
            };
            StorageItem mobilityItem = new StorageItem()
            {
                AbstractLocation = FILE_DIRECTORY,
                FileName = C_DLS_MOBILITY_FILENANME
            };

            if (
                await _googleCloudClient
                .FileExistsAsync( ipRangeItem, cancellationToken
                )

            )
            {
                using (MemoryStream fileIpRange = new MemoryStream())
                {
                    await _googleCloudClient
                        .DownloadFileAsync( ipRangeItem, fileIpRange, cancellationToken
                        );

                    fileIpRange.Position = 0;

                    ipRanges =
                        CsvImporter

                        .FromStream<DlsIpRange>( fileIpRange, "|"
                        );
                }
            }
            else
            {
                _logger.LogWarning($"No DLS IpRange File found at {ipRangeItem.ToString()}");
            }

            if (
                await _googleCloudClient
                .FileExistsAsync( ipClientItem, cancellationToken
                )

            )
            {
                using (MemoryStream fileIpClient = new MemoryStream())
                {
                    await _googleCloudClient
                        .DownloadFileAsync( ipClientItem, fileIpClient, cancellationToken
                        );
                    fileIpClient.Position = 0;

                    ipClients =
                        CsvImporter

                        .FromStream<DlsIpClient>( fileIpClient, "|"
                        );
                }
            }
            else
            {
                _logger.LogWarning($"No DLS IpClient File found at {ipClientItem.ToString()}");
            }

            if (
                await _googleCloudClient
                .FileExistsAsync( portItem, cancellationToken
                )

            )
            {
                using (MemoryStream filePort = new MemoryStream())
                {
                    await _googleCloudClient
                        .DownloadFileAsync( portItem, filePort, cancellationToken
                        );
                    filePort.Position = 0;

                    ports =
                        CsvImporter

                        .FromStream<DlsPort>( filePort, "|"
                        );
                }
            }
            else
            {
                _logger.LogWarning($"No DLS Port File found at {portItem.ToString()}");
            }

            if (
                await _googleCloudClient
                .FileExistsAsync( mobilityItem, cancellationToken
                )

            )
            {
                using (MemoryStream fileMobility = new MemoryStream())
                {
                    await _googleCloudClient
                        .DownloadFileAsync( mobilityItem, fileMobility, cancellationToken
                        );
                    fileMobility.Position = 0;

                    mobilities =
                        CsvImporter

                        .FromStream<DlsMobility>( fileMobility, "|"
                        );
                }
            }
            else
            {
                _logger.LogWarning($"No DLS Mobility File found at {mobilityItem.ToString()}");
            }

            _logger.LogInformation($"Retrieved {ipRanges.Count} ipRanges entries ");
            _logger.LogInformation($"Retrieved {ipClients.Count} ipClient entries");
            _logger.LogInformation($"Retrieved {mobilities.Count} mobilities entries");
            _logger.LogInformation($"Retrieved {ports.Count} ports entries");

            #endregion

            #region Delete all old data sets.

            if (ipClients.Count > 0)
                await repDataContext
                    .DlsIpClients
                    .ExecuteDeleteAsync(cancellationToken);
            if (ipRanges.Count > 0)
                await repDataContext
                    .DlsIpRanges
                    .ExecuteDeleteAsync(cancellationToken);
            if (mobilities.Count > 0)
                await repDataContext
                    .DlsMobilities
                    .ExecuteDeleteAsync(cancellationToken);
            if (mobilities.Count > 0)
                await repDataContext
                    .DlsPorts
                    .ExecuteDeleteAsync(cancellationToken);

            #endregion

            #region Add the new data to all tables.

            await repDataContext
                .DlsIpRanges
                .AddRangeAsync(ipRanges, cancellationToken);

            await repDataContext
                .DlsIpClients
                .AddRangeAsync(ipClients, cancellationToken);

            await repDataContext
                .DlsMobilities
                .AddRangeAsync(mobilities, cancellationToken);

            await repDataContext
                .DlsPorts
                .AddRangeAsync(ports, cancellationToken);

            #endregion

            await repDataContext
                .SaveChangesAsync(cancellationToken);

            _logger.LogInformation($"Saved Dls Data entries");

            #region Archive

            await _googleCloudClient
                .ArchiveFileAsync( portItem, cancellationToken: cancellationToken
                );
            await _googleCloudClient
                .ArchiveFileAsync( ipClientItem, cancellationToken: cancellationToken
                );
            await _googleCloudClient
                .ArchiveFileAsync( ipRangeItem, cancellationToken: cancellationToken
                );
            await _googleCloudClient
                .ArchiveFileAsync( mobilityItem, cancellationToken: cancellationToken
                );

            _logger.LogInformation("Archived DLS Files");

            #endregion
        }
    }
}
