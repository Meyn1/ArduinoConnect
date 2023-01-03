using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;
using Windows.Foundation;
using Windows.Security.Cryptography;
using Windows.Storage.Streams;

namespace ArduinoConnect
{
    /// <summary>
    /// Class to handle the Bluetooth Connection with the Ble device
    /// </summary>
    public class BLEHandler
    {
        private BluetoothLEDevice? _defaultDevice;
        private readonly List<DeviceInformation> _blueDevices = new();

        private readonly List<GattCharacteristic> _writeCharacs = new();
        private readonly List<GattCharacteristic> _readCharacs = new();
        private readonly List<GattCharacteristic> _notifyCharacs = new();
        private readonly List<GattCharacteristic> _readWriteCharacs = new();

        /// <summary>
        /// Write GattCharacteristic of the selected device
        /// </summary>
        public GattCharacteristic? DefaultWriteCharac { get; private set; }

        private static DeviceWatcher? _deviceWatcher;

        /// <summary>
        /// Watcher of the blootooth connection. Updates device list
        /// </summary>
        public static DeviceWatcher? DeviceWatcher => _deviceWatcher;

        /// <summary>
        /// Event that will notify if the Bluetooth status changed
        /// </summary>
        public static event EventHandler? BluetoothStateChanged;

        /// <summary>
        /// Event that will notify if the connection status to the connected device changed
        /// </summary>
        public event EventHandler? ConnectionStatusChanged;
        /// <summary>
        /// Event that will recive message if the ble device supports notification
        /// </summary>
        public event TypedEventHandler<GattCharacteristic, GattValueChangedEventArgs>? MessageRecived;

        /// <summary>
        /// Last created instance of the BLE handler
        /// </summary>
        public static BLEHandler? Instance { get; private set; }
        private static Radio? _blueRadio = null;
        private static bool _isBlueRadioScanned = false;

        private readonly string _aqsAllBLEDevices = "(System.Devices.Aep.ProtocolId:=\"{bb7bb05e-5972-42b5-94fc-76eaa7084d49}\")";
        private readonly string[] _requestedBLEProperties = { "System.Devices.Aep.DeviceAddress", "System.Devices.Aep.IsConnected" };

        /// <summary>
        /// Last connected device with this insttance of BLE Handler
        /// </summary>
        public BluetoothLEDevice? DefaultConnectedDevice => _defaultDevice;

        /// <summary>
        /// If the BLe handler scanned for ble devices 
        /// </summary>
        public bool IsScaned { get; private set; }

        /// <summary>
        /// If bluethooth is enabled
        /// </summary>
        public static bool IsBluetoothEnabled
        {
            get
            {
                if (!_isBlueRadioScanned)
                    ScanBluetoothRadio().Wait();
                return _blueRadio?.State == RadioState.On;
            }
        }

        /// <summary>
        /// All found ble devices.
        /// This array updates permanent
        /// </summary>
        public DeviceInformation[] FoundDevices => _blueDevices.ToArray();

        /// <summary>
        /// All found devices with name and ID
        /// This array updates permanent
        /// </summary>
        public (string Name, string Id)[] FoundDeviceNames => _blueDevices.Where(d => !string.IsNullOrEmpty(d.Name)).Select(d => new ValueTuple<string, string>(d.Name, d.Id)).ToArray();

        /// <summary>
        /// Constructor of the BLE Handler
        /// </summary>
        public BLEHandler()
        {
            Instance = this;
            CreateDeviceWatcher();
        }


        private void CreateDeviceWatcher()
        {
            if (_deviceWatcher != null)
                return;
            _deviceWatcher = DeviceInformation.CreateWatcher(_aqsAllBLEDevices, _requestedBLEProperties, DeviceInformationKind.AssociationEndpoint);
            _deviceWatcher.Added += (DeviceWatcher sender, DeviceInformation devInfo) =>
            {
                if (_blueDevices.FirstOrDefault(d => d.Id.Equals(devInfo.Id) || d.Name.Equals(devInfo.Name)) == null)
                    _blueDevices.Add(devInfo);
                IsScaned = true;
            };
            _deviceWatcher.Removed += (DeviceWatcher sender, DeviceInformationUpdate devInfo) =>
            {
                DeviceInformation? deviceInformation = _blueDevices.FirstOrDefault(device => device.Id == devInfo.Id);
                if (deviceInformation != null)
                    _blueDevices.Remove(deviceInformation);
            };

            _deviceWatcher.EnumerationCompleted += (DeviceWatcher sender, object arg) => sender.Stop();
            _deviceWatcher.Stopped += (DeviceWatcher sender, object arg) => { IsScaned = false; _blueDevices.Clear(); sender.Start(); };
            _deviceWatcher.Start();
        }

        /// <summary>
        /// Stop scanning for devices
        /// </summary>
        public static void StopScan() => _deviceWatcher?.Stop();

        /// <summary>
        /// Start scanning for devices
        /// </summary>
        public static void StartScan() => _deviceWatcher?.Start();

        /// <summary>
        /// Scans for Bluetooth modul and enables all bluetooth functions
        /// </summary>
        /// <returns>A task to await</returns>
        public static async Task ScanBluetoothRadio()
        {
            IReadOnlyList<Radio> radios = await Radio.GetRadiosAsync();
            _blueRadio = radios.FirstOrDefault(radio => radio.Kind == RadioKind.Bluetooth);
            if (_blueRadio != null)
                _blueRadio.StateChanged += (s, o) => { BluetoothStateChanged?.Invoke(IsBluetoothEnabled, new()); };
            _isBlueRadioScanned = true;
            BluetoothStateChanged?.Invoke(IsBluetoothEnabled, new());
        }

        /// <summary>
        /// All defices thta are hidden
        /// </summary>
        /// <param name="id">Id of hidden device</param>
        /// <param name="name">Name of hidden device</param>
        /// <returns></returns>
        public DeviceInformation? GetUnknownDevices(string id, string name)
   => _blueDevices.FirstOrDefault(devInfo => devInfo.Id == id && devInfo.Name == name);

        /// <summary>
        /// Connect the Ble Handler to a device with the name of the device
        /// </summary>
        /// <param name="name">Name of device</param>
        /// <returns>Task to await bool that indicates success</returns>
        public async Task<bool> ConnectFromName(string name) => await ConnectFromId(_blueDevices.FirstOrDefault(devInfo => devInfo.Name == name)?.Id);
        /// <summary>
        /// Connect the Ble Handler to a device with the device info
        /// </summary>
        /// <param name="devInfo">Device Information of device</param>
        /// <returns>Task to await bool that indicates success</returns>
        public async Task<bool> ConnectFromDeviceInfo(DeviceInformation devInfo) => await ConnectFromId(devInfo.Id);

        /// <summary>
        /// Connect the Ble Handler to a device with the device id
        /// </summary>
        /// <param name="id">id of device</param>
        /// <returns>Task to await bool that indicates success</returns>
        public async Task<bool> ConnectFromId(string? id)
        {
            if (!string.IsNullOrWhiteSpace(id))
                return await Connect(await BluetoothLEDevice.FromIdAsync(id));
            else return false;
        }

        /// <summary>
        /// Connect the Ble Handler to a device with the device adress
        /// </summary>
        /// <param name="adress">Device adress</param>
        /// <returns>Task to await bool that indicates success</returns>
        public async Task<bool> ConnectFromAdress(ulong adress) => await Connect(await BluetoothLEDevice.FromBluetoothAddressAsync(adress));

        /// <summary>
        /// Connect the Ble Handler to a device with the ble device
        /// </summary>
        /// <param name="bleDevice">Bluetooth BLE Device of device</param>
        /// <returns>Task to await bool that indicates success</returns>
        public async Task<bool> Connect(BluetoothLEDevice bleDevice)
        {
            if (bleDevice == null)
                return false;
            _writeCharacs.Clear();
            _readCharacs.Clear();
            _readWriteCharacs.Clear();
            try
            {
                GattDeviceServicesResult serviceResult = await bleDevice.GetGattServicesAsync(BluetoothCacheMode.Uncached);
                if (serviceResult.Status != GattCommunicationStatus.Success)
                    return false;
                bool success = false;
                bleDevice.ConnectionStatusChanged += ConnectionChanged;
                IReadOnlyList<GattDeviceService> services = serviceResult.Services;
                foreach (GattDeviceService service in services)
                {
                    GattCharacteristicsResult characResult = await service.GetCharacteristicsAsync(BluetoothCacheMode.Uncached);
                    if (characResult.Status == GattCommunicationStatus.Success)
                    {
                        IReadOnlyList<GattCharacteristic> characs = characResult.Characteristics;
                        if (characs.Count > 0)
                            foreach (GattCharacteristic? charac in characs)
                            {

                                if (charac.Uuid == Guid.Parse("0000ffe1-0000-1000-8000-00805f9b34fb"))
                                    _readWriteCharacs.Add(charac);
                                if (CanRead(charac))
                                    _readCharacs.Add(charac);
                                if (CanWrite(charac))
                                    _writeCharacs.Add(charac);
                                try
                                {
                                    if (CanNotify(charac) && OnMessageRecived != null && await charac.WriteClientCharacteristicConfigurationDescriptorAsync(
                                                                GattClientCharacteristicConfigurationDescriptorValue.Notify) == 0)
                                    {
                                        charac.ValueChanged += OnMessageRecived;
                                        _notifyCharacs.Add(charac);
                                    }
                                }
                                catch (Exception)
                                {
                                }
                                success = true;

                            }
                    }
                }
                if (success)
                {
                    DefaultWriteCharac = _readWriteCharacs.FirstOrDefault() ?? _writeCharacs.FirstOrDefault();
                    _defaultDevice = bleDevice;
                }
                return success;
            }
            catch (Exception) { }
            return false;
        }

        private void OnMessageRecived(GattCharacteristic sender, GattValueChangedEventArgs args) => MessageRecived?.Invoke(sender, args);

        private void ConnectionChanged(BluetoothLEDevice sender, object args) => ConnectionStatusChanged?.Invoke(sender.ConnectionStatus == BluetoothConnectionStatus.Connected, new());



        /// <summary>
        /// Send an byte array to the default device
        /// </summary>
        /// <param name="data">Data to send</param>
        /// <returns>Task to await bool that indicates success</returns>
        public async Task<bool> Send(byte[] data) => await Send(data.AsBuffer());

        /// <summary>
        /// Send a string to the default connected device
        /// </summary>
        /// <param name="data">String to send</param>
        /// <returns>Task to await bool that indicates success</returns>
        public async Task<bool> Send(string data) => await Send(CryptographicBuffer.ConvertStringToBinary(data,
                    BinaryStringEncoding.Utf8));

        /// <summary>
        /// Send a IBuffer to the default connected device
        /// </summary>
        /// <param name="buffer">Buffer to send</param>
        /// <returns>Task to await bool that indicates success</returns>
        public async Task<bool> Send(IBuffer buffer) => await Send(buffer, DefaultWriteCharac ?? _readWriteCharacs.FirstOrDefault() ?? _writeCharacs.FirstOrDefault()!);

        /// <summary>
        /// Send a IBuffer to an gatt characteristic
        /// </summary>
        /// <param name="buffer">buffer to send</param>
        /// <param name="characteristic">characteristic to send the buffer</param>
        /// <returns>Task to await bool that indicates success</returns>
        /// <exception cref="ArgumentNullException">Throws if characteristic is null</exception>
        /// <exception cref="NotSupportedException">Throws if the caracteristic can not write data</exception>
        public static async Task<bool> Send(IBuffer buffer, GattCharacteristic characteristic)
        {
            _ = characteristic ?? throw new ArgumentNullException(nameof(characteristic));
            if (!CanWrite(characteristic))
                throw new NotSupportedException(nameof(characteristic));
            return (await characteristic.WriteValueWithResultAsync(buffer)).Status == GattCommunicationStatus.Success;
        }

        /// <summary>
        /// Disconnect default device
        /// </summary>
        public void Disconnect()
        {
            try
            {
                if (_defaultDevice != null)
                {
                    _defaultDevice.ConnectionStatusChanged -= ConnectionChanged;
                    ConnectionStatusChanged?.Invoke(false, new());
                }
                _notifyCharacs.ForEach(charac => { try { charac.ValueChanged -= OnMessageRecived; if (charac.Service.Session.SessionStatus == GattSessionStatus.Active) charac.Service.Dispose(); } catch (Exception) { } });
                _notifyCharacs.Clear();
                _readCharacs.ForEach(charac => { try { if (charac.Service.Session.SessionStatus == GattSessionStatus.Active) charac.Service.Dispose(); } catch (Exception) { } });
                _readCharacs.Clear();
                _readWriteCharacs.ForEach(charac => { try { if (charac.Service.Session.SessionStatus == GattSessionStatus.Active) charac.Service.Dispose(); } catch (Exception) { } });
                _readWriteCharacs.Clear();
                _writeCharacs.ForEach(charac => { try { if (charac.Service.Session.SessionStatus == GattSessionStatus.Active) charac.Service.Dispose(); } catch (Exception) { } });
                _writeCharacs.Clear();
                _defaultDevice?.GattServices.ToList().ForEach(x => x.Dispose());
                if (_defaultDevice != null)
                {
                    _defaultDevice.Dispose();
                    _defaultDevice = null;
                }
                DefaultWriteCharac = null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
        }

        /// <summary>
        /// Is the device connectable
        /// </summary>
        /// <param name="devInfo">Device to proof</param>
        /// <returns>bool that indicates success</returns>
        public static bool IsConnectable(DeviceInformation devInfo) =>
        string.IsNullOrEmpty(devInfo.Name) && ((bool)devInfo.Properties["System.Devices.Aep.Bluetooth.Le.IsConnectable"] == true
            || (bool)devInfo.Properties["System.Devices.Aep.IsConnected"] == true);


        /// <summary>
        /// Enable bluetooth
        /// </summary>
        /// <returns>Task to await bool that indicates success</returns>
        public static async Task<bool> EnableBluetoothAsync() => await EnableBluetoothAsync(true);

        /// <summary>
        /// Disable Bluetooth
        /// </summary>
        /// <returns>Task to await bool that indicates success</returns>
        public static async Task<bool> DisableBluetoothAsync() => await EnableBluetoothAsync(false);

        private static async Task<bool> EnableBluetoothAsync(bool enable)
        {
            RadioAccessStatus result = await Radio.RequestAccessAsync();
            if (result == RadioAccessStatus.Allowed)
            {
                if (!_isBlueRadioScanned)
                    await ScanBluetoothRadio();
                if (_blueRadio?.State != (enable ? RadioState.On : RadioState.Off))
                    await _blueRadio?.SetStateAsync(enable ? RadioState.On : RadioState.Off);
                return true;
            }
            return false;
        }

        private static bool CanWrite(GattCharacteristic characteristic) => characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Write) ||
                 characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.WriteWithoutResponse) ||
                 characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.ReliableWrites) ||
                 characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.WritableAuxiliaries);
        private static bool CanRead(GattCharacteristic characteristic) => characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Read);
        private static bool CanNotify(GattCharacteristic characteristic) => characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Notify);
    }
}
