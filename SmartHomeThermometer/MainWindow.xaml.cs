using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace SmartHomeThermometer
{
    public partial class MainWindow : Window
    {
        // Размер буфера для принимаемых данных.
        private static readonly int BUFFER_SIZE = 8192;
        // Разделитель между элементами данных.
        private static readonly char DELIMITER = ';';
        // Надпись элемента интерфейса.
        private static readonly string IPADDRESS_LOG_LABEL = "IP Address: ";
        // Надпись элемента интерфейса.
        private static readonly string PORT_LOG_LABEL = "Port: ";
        // Минимальное и максимальное значения используемого порта.
        private static readonly int MINIMAL_PORT_VALUE = 1024;
        private static readonly int MAXIMAL_PORT_VALUE = 49151;
        // Метка устройства для журнала.
        private static readonly string THERMOMETER_LOG_LABEL = "Thermometer: ";
        // Метка подключения для журнала.
        private static readonly string CONNECTION_LOG_LABEL = "Connection: ";
        // Состояния подключения устройства.
        private static readonly string CONNECTION_UP = "up";
        private static readonly string CONNECTION_WAIT = "wait";
        private static readonly string CONNECTION_DOWN = "down";
        private static readonly string CONNECTION_ERR = "err";
        // Метка периода обновления данных для журнала.
        private static readonly string UPDATE_INTERVAL_LOG_LABEL = "Update interval: ";
        // Метка сети для журнала.
        private static readonly string NETWORK_LOG_LABEL = "Network: ";
        // Аргумент типа устройства.
        private static readonly string NETWORK_DEVICE_ARG = "Device: ";
        // Аргумент температуры.
        private static readonly string NETWORK_TEMPERATURE_ARG = "Temperatute: ";
        // Аргумент периода обновления.
        private static readonly string NETWORK_UPDATE_INTERVAL_ARG = "Update interval: ";
        // Аргумент метода для исполнения.
        private static readonly string NETWORK_METHOD_TO_INVOKE_ARG = "Method: ";
        // Аргумент состояния работы устройства.
        private static readonly string NETWORK_STATUS_ARG = "Status: ";
        // Метод для обновления температуры.
        private static readonly string NETWORK_METHOD_TO_UPDATE_TEMP = "UPDATE_TEMP";
        // Метод для отключения устройства.
        private static readonly string NETWORK_METHOD_TO_DISCONNECT = "DISCONNECT";
        // Метод для запроса состояния работы устройства.
        private static readonly string NETWORK_METHOD_TO_REQUEST_STATUS = "REQUEST_STATUS";

        // Корректный статус работы устройства.
        private static readonly int DEVICE_STATUS_UP = 42;
        // Расширенные уровень логгирования.
        private bool _VerboseLogging;
        // Автоматическая прокрутка журанала.
        private bool _ShouldScrollToEnd;
        // Класс, предоставляющий данные термометра.
        private Thermometer _Thermometer;
        // Период обновления температуры.
        private int _UpdateInterval;
        // Сокет.
        private TcpClient _Socket;
        // Поток, принимающий и обрабатывающий данные от сервера.
        private Thread _ListenerThread;
        // Мьютекс для синхронизации обращения к данным.
        private Mutex _DataMutex;
        // Кэш данные, полученных от сервера.
        private List<string> _Cache;
        // IP-адрес и порт сервера.
        private IPAddress _IPAddress;
        private int _Port;

        public MainWindow()
        {
            InitializeComponent();
            // Инициализация и конфигурация программы.
            Init();
            Configure();
        }
        // Инициализация объектов.
        private void Init()
        {
            _Thermometer = new Thermometer();

            _UpdateInterval = Thermometer.DEFAULT_UPDATE_INTERVAL;
            UpdateIntervalTextBlock.Text = _UpdateInterval.ToString();

            _DataMutex = new Mutex();

            _Cache = new List<string>();
        }
        // Настройка объектов.
        private void Configure()
        {
            _VerboseLogging = false;
            VerobseLoggingCheckBox.IsChecked = _VerboseLogging;
            VerobseLoggingCheckBox.Checked += (sender, e) =>
            {
                _VerboseLogging = true;
            };
            VerobseLoggingCheckBox.Unchecked += (sender, e) =>
            {
                _VerboseLogging = false;
            };

            _ShouldScrollToEnd = true;
            ScrollToEndCheckBox.IsChecked = _ShouldScrollToEnd;
            ScrollToEndCheckBox.Checked += (sender, e) =>
            {
                _ShouldScrollToEnd = true;
            };
            ScrollToEndCheckBox.Unchecked += (sender, e) =>
            {
                _ShouldScrollToEnd = false;
            };

            /// App
            // Закрытие программы.
            Closed += (sender, e) =>
            {
                _Thermometer.Dispose();
                Disconnect();
                _Socket = null;
            };

            /// Controls
            // Кнопка подключения.
            ConnectButton.IsEnabled = true;
            ConnectButton.Click += (sender, e) =>
            {
                Connect();
            };
            // Кнопка отключения.
            DisconnectButton.IsEnabled = false;
            DisconnectButton.Click += (sender, e) =>
            {
                Disconnect();

                /// Bad idea due to bad design.
                _Socket = new TcpClient();
            };
            // Кнопка обновления периода обновления.
            UpdateIntervalSetButton.Click += (sender, e) =>
            {
                try
                {
                    _UpdateInterval = int.Parse(UpdateIntervalTextBlock.Text);
                    _Thermometer.UpdateInterval = _UpdateInterval;

                    Log(UPDATE_INTERVAL_LOG_LABEL + string.Format("Set to {0}" + '\n', _UpdateInterval));

                    if (_Socket != null && _Socket.Connected)
                    {
                        SendUpdateInterval(_Thermometer.UpdateInterval);
                    }
                }
                catch (Exception exc)
                {
                    if (_VerboseLogging)
                    {
                        Log(UPDATE_INTERVAL_LOG_LABEL + exc.Message + '\n');
                    }
                }
            };
            // Кнопка обновления температуры.
            TemperatureUpdateButton.Click += (sender, e) =>
            {
                _Thermometer.UpdateTemperature();
            };

            /// Objects
            // callback объекта, предоставляющего данные о температуре,
            // вызываемый при обновлении температуры.
            _Thermometer.OnTemperatureUpdate = (temperature) =>
            {
                Dispatcher.Invoke(delegate ()
                {
                    TemperatureValueLabel.Content = temperature.ToString("F2");
                });

                if (_Socket != null && _Socket.Connected)
                {
                    SendTemperature(temperature);
                }
            };
        }
        // Настройка потока, принимающего и обрабатывающего данные от сервера.
        private Thread ConfigureListenerThread()
        {
            return new Thread(new ThreadStart(delegate ()
            {
                try
                {
                    while (_Socket != null && _Socket.Connected)
                    {
                        // Принять данные.
                        byte[] bytes = new byte[BUFFER_SIZE];
                        Receive(ref _Socket, ref bytes);
                        // Закэшировать и обработать данные.
                        ProcessData(CacheData(Encoding.Unicode.GetString(bytes), ref _Cache));
                        ProcessData(ref _Cache);
                    }
                }
                catch (ThreadAbortException)
                {
                    Log(NETWORK_LOG_LABEL + "Disconnected." + '\n');
                    if (_VerboseLogging)
                    {
                        Log(NETWORK_LOG_LABEL + "Listener thread was terminated" + '\n');
                    }
                }
            }));
        }
        // Настройка потока, осуществляющего подключение.
        private Thread ConfigureConnectThread()
        {
            return new Thread(new ThreadStart(delegate ()
            {
                // Обновление статуса подключения на ожидание.
                Dispatcher.Invoke(delegate ()
                {
                    ConnectionStateLabel.Content = CONNECTION_WAIT;
                    SwitchButtonsOnConnectionStatusChanged(true);
                });
                Log((CONNECTION_LOG_LABEL +
                    string.Format("Connecting to {0}:{1}\n", _IPAddress.ToString(), _Port)));

                try
                {
                    // Подключение по заданным адресу и порту.
                    _Socket = new TcpClient();
                    _Socket.Connect(_IPAddress, _Port);
                    // Обновление статуса при успешном подключении.
                    Dispatcher.Invoke(delegate ()
                    {
                        ConnectionStateLabel.Content = CONNECTION_UP;
                    });
                    Log(CONNECTION_LOG_LABEL +
                        string.Format("Connected to {0}:{1}\n", _IPAddress.ToString(), _Port));
                    // Отправить данные об устройстве.
                    SendInfo();
                    // Отправить период обновления температуры.
                    SendUpdateInterval(_UpdateInterval);
                    // Запуск потока, принимающего и обрабатывающего данные.
                    _ListenerThread = ConfigureListenerThread();
                    _ListenerThread.Start();
                }
                catch (SocketException exc)
                {
                    // Ошибка подключения.
                    Dispatcher.Invoke(delegate ()
                    {
                        ConnectionStateLabel.Content = CONNECTION_ERR;
                        SwitchButtonsOnConnectionStatusChanged(false);
                    });
                    if (_VerboseLogging)
                    {
                        Log(CONNECTION_LOG_LABEL + exc.Message + '\n');
                    }
                }
                catch (ObjectDisposedException exc)
                {
                    // Закрытие подключения.
                    Dispatcher.Invoke(delegate ()
                    {
                        ConnectionStateLabel.Content = CONNECTION_DOWN;
                        SwitchButtonsOnConnectionStatusChanged(false);
                    });
                    if (_VerboseLogging)
                    {
                        Log(CONNECTION_LOG_LABEL + exc.Message + '\n');
                    }
                }
            }));
        }
        // Подключение к серверу.
        private void Connect()
        {
            // Прочитать IP-адрес.
            try
            {
                _IPAddress = IPAddress.Parse(AddressTextBox.Text);
            }
            catch (Exception exc)
            {
                Log(IPADDRESS_LOG_LABEL + exc.Message + '\n');
                return;
            }
            // Прочитать порт.
            try
            {
                _Port = int.Parse(PortTextBox.Text);

                if (_Port < MINIMAL_PORT_VALUE || _Port > MAXIMAL_PORT_VALUE)
                {
                    throw new Exception(string.Format("Incorrect port value. [{0}; {1}] ports are allowed.",
                        MINIMAL_PORT_VALUE, MAXIMAL_PORT_VALUE));
                }
            }
            catch (Exception exc)
            {
                Log(PORT_LOG_LABEL + exc.Message + '\n');
                return;
            }
            // Запуск потока, осуществляющего подключение.
            Thread connectThread = ConfigureConnectThread();
            connectThread.Start();
        }

        // Отключение.
        private void Disconnect()
        {
            // Отправить метод для отключения устройства.
            SendMethodToInvoke(NETWORK_METHOD_TO_DISCONNECT);
            // Завершить поток, принимающий и обрабатывающий данные от устройства.
            if (_ListenerThread.IsAlive)
            {
                _ListenerThread.Abort();
            }
            // Закрыть сокет.
            if (_Socket != null)
            {
                if (_Socket.Connected)
                {
                    _Socket.Close();
                }
                else
                {
                    _Socket.Dispose();
                }
            }
            // Обновить интерфейс.
            SwitchButtonsOnConnectionStatusChanged(false);
            if (_VerboseLogging)
            {
                Log(CONNECTION_LOG_LABEL + "Connection was manually closed" + '\n');
            }
        }
        // Обновить состояние кнопок в зависимости от статуса подключения.
        private void SwitchButtonsOnConnectionStatusChanged(bool isConnected)
        {
            Dispatcher.Invoke(delegate ()
            {
                PortTextBox.IsEnabled = !isConnected;

                ConnectButton.IsEnabled = !isConnected;
                DisconnectButton.IsEnabled = isConnected;
            });
        }
        // Отправить данные.
        private void Send(byte[] bytes)
        {
            if (_Socket == null)
            {
                return;
            }
            try
            {
                NetworkStream stream = _Socket.GetStream();
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush();
            }
            catch (System.IO.IOException exc)
            {
                SwitchButtonsOnConnectionStatusChanged(false);
                if (_VerboseLogging)
                {
                    Log(NETWORK_LOG_LABEL +
                        (exc.InnerException != null ? exc.InnerException.Message : exc.Message) + '\n');
                }
                else
                {
                    Log(CONNECTION_LOG_LABEL + "Connection's unavailable." + '\n');
                }
            }
        }
        // Получить данные.
        private void Receive(ref TcpClient socket, ref byte[] bytes)
        {
            if (_Socket == null)
            {
                return;
            }

            try
            {
                NetworkStream stream = socket.GetStream();
                stream.Read(bytes, 0, socket.ReceiveBufferSize);
            }
            catch (System.IO.IOException exc)
            {
                SwitchButtonsOnConnectionStatusChanged(false);
                if (_VerboseLogging)
                {
                    Log(NETWORK_LOG_LABEL +
                        (exc.InnerException != null ? exc.InnerException.Message : exc.Message) + '\n');
                }
                else
                {
                    Log(CONNECTION_LOG_LABEL + "Connection's unavailable." + '\n');
                }
            }
        }
        // Отправить данные об устройстве.
        private void SendInfo()
        {
            byte[] bytes = Encoding.Unicode.GetBytes(NETWORK_DEVICE_ARG + "Thermometer" + DELIMITER);
            Send(bytes);

            Log(NETWORK_LOG_LABEL + "Sent info" + '\n');
        }
        // Отправить значение периода обновления.
        private void SendUpdateInterval(double updateInterval)
        {
            byte[] bytes = Encoding.Unicode.GetBytes(string.Format(NETWORK_UPDATE_INTERVAL_ARG + "{0}" + DELIMITER, updateInterval));
            Send(bytes);

            Log(NETWORK_LOG_LABEL + "Sent update interval" + '\n');
        }
        // Отправить температуру.
        private void SendTemperature(double temperature)
        {
            byte[] bytes = Encoding.Unicode.GetBytes(string.Format(NETWORK_TEMPERATURE_ARG + "{0}" + DELIMITER, temperature));
            Send(bytes);

            Log(NETWORK_LOG_LABEL +
                string.Format("Sent temperature: {0}", temperature.ToString("F2")) + '\n');
        }
        // Отправить состояние работы устройства.
        private void SendStatus()
        {
            byte[] bytes = Encoding.Unicode.GetBytes(string.Format(NETWORK_STATUS_ARG + "{0}" + DELIMITER, DEVICE_STATUS_UP));
            Send(bytes);

            if (_VerboseLogging)
            {
                Log(NETWORK_LOG_LABEL + string.Format("Sent status: {0}", DEVICE_STATUS_UP) + '\n');
            }
        }
        // Отправить метод для исполнения.
        private void SendMethodToInvoke(string method)
        {
            byte[] bytes = Encoding.Unicode.GetBytes(NETWORK_METHOD_TO_INVOKE_ARG + method + DELIMITER);
            Send(bytes);

            if (_VerboseLogging)
            {
                Log(NETWORK_LOG_LABEL + "Sent method: " + method + '\n');
            }
        }
        // Закэшировать данные.
        string CacheData(string data, ref List<string> cache)
        {
            // Подробно описано в сервере.
            int delimiterIdx = data.IndexOf(DELIMITER);
            string first = data.Substring(0, delimiterIdx + 1);

            data = data.Substring(delimiterIdx + 1, data.Length - delimiterIdx - 1);
            for (delimiterIdx = data.IndexOf(DELIMITER); delimiterIdx >= 0; delimiterIdx = data.IndexOf(DELIMITER))
            {
                cache.Add(data.Substring(0, delimiterIdx + 1));
                data = data.Substring(delimiterIdx + 1, data.Length - delimiterIdx - 1);
            }

            return first;
        }
        // Обработка элемента данных.
        private void ProcessData(string data)
        {
            if (string.IsNullOrEmpty(data) || data.Equals(""))
            {
                return;
            }

            int idx;
            // Период обновления данных.
            if ((idx = data.IndexOf(NETWORK_UPDATE_INTERVAL_ARG)) >= 0)
            {
                try
                {
                    // Чтение значения.
                    int startIdx = idx + NETWORK_UPDATE_INTERVAL_ARG.Length, endIdx = data.IndexOf(DELIMITER);
                    int updateInterval = int.Parse(data.Substring(startIdx, endIdx - startIdx));

                    Log(NETWORK_LOG_LABEL + string.Format("Received update interval: {0}", updateInterval) + '\n');
                    // Обновление значения.
                    try
                    {
                        _Thermometer.UpdateInterval = updateInterval;

                        Dispatcher.Invoke(delegate ()
                        {
                            UpdateIntervalTextBlock.Text = updateInterval.ToString();
                        });
                    }
                    catch (Exception exc)
                    {
                        // В случае ошибки отправить текущий установленный период.
                        SendUpdateInterval(_Thermometer.UpdateInterval);

                        Log(UPDATE_INTERVAL_LOG_LABEL + exc.Message + '\n');
                    }
                }
                catch (FormatException)
                {
                    Log(NETWORK_LOG_LABEL + "Received incorrect update interval" + '\n');
                }
            }
            // Метод для исполнения.
            else if ((idx = data.IndexOf(NETWORK_METHOD_TO_INVOKE_ARG)) >= 0)
            {
                int startIdx = idx + NETWORK_METHOD_TO_INVOKE_ARG.Length, endIdx = data.IndexOf(DELIMITER);
                string method = data.Substring(startIdx, endIdx - startIdx);
                // Обновить температуру.
                if (!string.IsNullOrEmpty(method) && method.Equals(NETWORK_METHOD_TO_UPDATE_TEMP))
                {
                    _Thermometer.UpdateTemperature();

                    Log(NETWORK_LOG_LABEL + "Temperature update was requested." + '\n');
                }
                // Получить состояние работы устройства.
                else if (!string.IsNullOrEmpty(method) && method.Equals(NETWORK_METHOD_TO_REQUEST_STATUS))
                {
                    SendStatus();

                    if (_VerboseLogging)
                    {
                        Log(NETWORK_LOG_LABEL + "Status was requested." + '\n');
                    }
                }
            }
            else
            {
                Log(string.Format(NETWORK_LOG_LABEL + "Received unknown data: \"{0}\"" + '\n', data));
            }
        }
        // Обработать список элементов данных.
        private void ProcessData(ref List<string> dataSet)
        {
            _DataMutex.WaitOne();

            foreach (string data in dataSet)
            {
                ProcessData(data);
            }

            dataSet.Clear();

            _DataMutex.ReleaseMutex();
        }

        // Добавление записи в журнал.
        private void Log(string info)
        {
            try
            {
                Dispatcher.Invoke(delegate ()
                {
                    LogTextBlock.AppendText(info);
                    if (_ShouldScrollToEnd)
                    {
                        LogTextBlock.ScrollToEnd();
                    }
                });
            }
            catch (TaskCanceledException)
            {
                return;
            }
        }
    }
}
