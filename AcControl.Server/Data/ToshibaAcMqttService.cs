using System.Text.Json;
using Microsoft.Azure.Devices.Client;

namespace AcControl.Server.Data
{
    public class ToshibaAcMqttService
    {
        private readonly ToshibaAcHttpService mToshibaAcHttpService;
        private readonly Lazy<Task<DeviceClient>> mDeviceClient;

        public ToshibaAcMqttService(ToshibaAcHttpService toshibaAcHttpService)
        {
            mToshibaAcHttpService = toshibaAcHttpService;
            mDeviceClient = new(async () =>
            {
                var deviceRegistration = await toshibaAcHttpService.RegisterDevice(CancellationToken.None);

                var connectionString = $"HostName={deviceRegistration.HostName};DeviceId={toshibaAcHttpService.DeviceId};SharedAccessKey={deviceRegistration.PrimaryKey}";
                var client = DeviceClient.CreateFromConnectionString(connectionString, TransportType.Amqp_WebSocket_Only);
                await client.SetMethodHandlerAsync("smmobile", this.DeviceClient_OnFCUCommandFromAC, null);
                await client.OpenAsync();
                return client;
            });
        }

        public async Task SetAcUnitState(string state, string targetId)
        {
            var command = new Command()
            {
                MessageId = Guid.NewGuid().ToString("N"),
                SourceId = mToshibaAcHttpService.DeviceId,
                TimeStamp = DateTime.UtcNow.TimeOfDay.ToString(),
                Cmd = "CMD_FCU_TO_AC",
                Payload = new() { Data = state },
                TargetId = new[] { targetId },
            };

            await this.SendCommand(command);
        }

        private Task<MethodResponse> DeviceClient_OnFCUCommandFromAC(MethodRequest methodRequest, object userContext)
        {
            Console.WriteLine("Device In: " + methodRequest.DataAsJson);

            return Task.FromResult(new MethodResponse(200));
        }

        private async Task SendCommand(Command command)
        {
            var message = new Message(JsonSerializer.SerializeToUtf8Bytes(command));
            message.Properties["type"] = "mob";
            message.ContentType = "application/json";
            message.ContentEncoding = "utf-8";

            var client = await mDeviceClient.Value;
            await client.SendEventAsync(message);
        }

        private class Command
        {
            public string TimeStamp { get; set; } = "";

            public string MessageId { get; set; } = "";

            public string SourceId { get; set; } = "";

            public string[] TargetId { get; set; } = Array.Empty<string>();

            public string Cmd { get; set; } = "";

            public Payload? Payload { get; set; }
        }

        private class Payload
        {
            public string Data { get; set; } = "";
        }
    }
}
