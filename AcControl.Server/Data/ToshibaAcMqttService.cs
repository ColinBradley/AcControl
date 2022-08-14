using System.Text.Json;
using Microsoft.Azure.Devices.Client;

namespace AcControl.Server.Data
{
    public class ToshibaAcMqttService
    {
        private readonly ToshibaAcHttpService mToshibaAcHttpService;
        private readonly Lazy<Task<DeviceClient>> mDeviceClient;

        public delegate void DeviceUpdateHandler(string deviceId, string state);

        public event DeviceUpdateHandler? DeviceUpdated;

        public ToshibaAcMqttService(ToshibaAcHttpService toshibaAcHttpService)
        {
            mToshibaAcHttpService = toshibaAcHttpService;
            mDeviceClient = new(async () =>
            {
                var deviceRegistration = await toshibaAcHttpService.RegisterDevice(CancellationToken.None);

                var connectionString = $"HostName={deviceRegistration!.HostName};DeviceId={toshibaAcHttpService.DeviceId};SharedAccessKey={deviceRegistration.PrimaryKey}";
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
            // Device In: { "sourceId":"e19ddd05-4f4c-4193-9f98-8f921d455f38","messageId":"0000000","targetId":["d33d7e54b66d4d1c8d066060041f1e00"],"cmd":"CMD_SET_SCHEDULE_FROM_AC","payload":{ "programSetting":{ "Sunday":{ "p1":"","p2":"","p3":"","p4":""},"Monday":{ "p1":"","p2":"","p3":"","p4":""},"Tuesday":{ "p1":"","p2":"","p3":"","p4":""},"Wednesday":{ "p1":"","p2":"","p3":"","p4":""},"Thursday":{ "p1":"","p2":"","p3":"","p4":""},"Friday":{ "p1":"","p2":"","p3":"","p4":""},"Saturday":{ "p1":"","p2":"","p3":"","p4":""} },"schedulerStatus":"00","dstStatus":"ON","dst":{ "Time":"1667095200","Status":"OFF"} },"timeStamp":"0000000"}
            // Device In: { "sourceId":"e19ddd05-4f4c-4193-9f98-8f921d455f38","messageId":"","targetId":["d33d7e54b66d4d1c8d066060041f1e00"],"cmd":"CMD_FCU_FROM_AC","payload":{ "data":"30411941316400101810fe0200001002000000"},"timeStamp":"11:15:58.2284097"}
            Console.WriteLine("Device In: " + methodRequest.DataAsJson);

            var command = JsonSerializer.Deserialize<Command>(methodRequest.DataAsJson, new JsonSerializerOptions() { PropertyNameCaseInsensitive = true });
            switch (command?.Cmd)
            {
                case "CMD_FCU_FROM_AC":
                    if (command.Payload?.Data.StartsWith("ff") ?? false)
                    {
                        // Odd states are sometimes sent with most values set to ff... ignore them?
                        break;
                    }

                    this.DeviceUpdated?.Invoke(command.SourceId, command.Payload!.Data);
                    break;
            }

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
            public string SourceId { get; set; } = "";

            public string MessageId { get; set; } = "";

            public string[] TargetId { get; set; } = Array.Empty<string>();

            public string Cmd { get; set; } = "";

            public Payload? Payload { get; set; }

            public string TimeStamp { get; set; } = "";
        }

        private class Payload
        {
            public string Data { get; set; } = "";
        }
    }
}
