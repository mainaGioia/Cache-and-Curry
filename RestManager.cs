using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Finetuner.Models.Administration;
using Huginn.Asgard.ApplicationsBackend.HttpClientAsync;
using Huginn.Asgard.ApplicationsBackend.DTOAbstractions;
using Plugin.Connectivity;
using System.Net.Http;
using Fusillade;
using ModernHttpClient;
using System.Linq;
using Polly;
using System.Net;
using Finetuner.ViewModels;
using Huginn.Asgard.ApplicationsBackend.HttpClientAsync.Factory;
using System.Collections.Concurrent;
using Finetuner.Services.Storage.Utils;
using System.Collections.Specialized;
using System.Collections.ObjectModel;
using Plugin.Connectivity.Abstractions;

namespace Finetuner.Services.Storage
{
    public class RestManager
    {


        class FinetunerClient : IFinetunerRestApi
        {
            static string baseUri = "http://mobile-app-api.medel.cloud/api/";
            IClient client;

            public FinetunerClient(HttpMessageHandler messageHandler)
            {
                // TODO : need to pass a parameter to the HttpClientit was sn
                //client = new HttpClientFactory(baseUri, messageHandler).GetClient();
                client = new HttpClientFactory(baseUri).GetClient();
            }


            public async Task<List<MedicalDevice>> GetRemoteMedicalDevices(User user)
            {
                List<MedicalDevice> medDevices = new List<MedicalDevice>();
                IEnumerable<IMedicalDevice> iMedDevices = await client.MedicalDevice.GetMany(user.UserToken);
                //TO BE MOVED IN STORAGEFACADE! 
                if (iMedDevices.Any())
                    foreach (IMedicalDevice imd in iMedDevices)
                        medDevices.Add(new MedicalDevice(imd, user.UserToken, user.FirstName, user.CacheKey));
                return medDevices;
            }

            public async Task<MedicalDevice> GetRemoteMedicalDevice(MedicalDevice md) { return new MedicalDevice(await client.MedicalDevice.Get(md.SerialNumber, md.TypeNumber), md.UserToken, md.LinkedUserName, md.LinkedUserKey); }


            public async Task<User> GetRemoteUser(Guid accessToken) { return new User(await client.User.Get(accessToken)); }

            //public async Task<List<User>> GetRemoteUsers(IResult result)
            //{
            //    List<User> users = new List<User>();
            //    User user = new User(await client.User.Get(result.UserAccessToken));
            //    if (user != null)
            //    {
            //        user.UserToken = result.UserAccessToken;
            //        if (result.ChildUsers != null && result.ChildUsers.Any())
            //        {
            //            user.IsGuardian = true;
            //            users.Add(user);
            //            foreach (Guid childToken in result.ChildUsers)
            //            {
            //                User child = new User(await client.User.Get(childToken));
            //                child.UserToken = childToken;
            //                user.ChildUserList.Add(child);
            //                users.Add(child);
            //            }
            //        }
            //        else
            //            users.Add(user);
            //    }

            //    return users;                
            //}


            public async Task<List<User>> GetRemoteUsersAndDevices(ILoginResult result)
            {
                List<User> users = new List<User>();
                User user = new User(await client.User.Get(result.UserAccessToken));
                if (user != null)
                {
                    user.UserToken = result.UserAccessToken;
                    if (result.ChildUsers != null && result.ChildUsers.Any())
                    {
                        user.IsGuardian = true;
                        users.Add(user);
                        foreach (Guid childToken in result.ChildUsers)
                        {
                            User child = new User(await client.User.Get(childToken));
                            child.UserToken = childToken;
                            user.ChildUserList.Add(child);
                            users.Add(child);
                        }
                    }
                    else
                        users.Add(user);

                    //retrieve the medical devices for each user
                    foreach (User u in users)
                    {
                        List<MedicalDevice> userMedDevices = await GetRemoteMedicalDevices(u);
                        if (userMedDevices != null && userMedDevices.Any())
                            foreach (MedicalDevice medDev in userMedDevices)
                                u.DevicesList.Add(medDev);
                    }
                }

                return users;
            }


            public async Task<ILoginResult> Login(string email, string password, string udid) { return await client.User.Login(email, password, udid); }

            public async Task<Guid> Create(User user, string udid) { return await client.User.Create(user, udid); }

            public async Task<Guid> Create(User user, Guid parentGuid) { return await client.User.Create(user, parentGuid); }

            public async Task<string> Create(Guid userToken, MedicalDevice md)      //string!??!!?!! 
            {
                System.Diagnostics.Debug.WriteLine($":::::::::: trying to create device for {userToken}::::::{md.SerialNumber} - {md.TypeNumber} - {md.DeviceName} - {md.DeviceDescription}");
                return await client.MedicalDevice.Create(userToken, md);
            }

            public async Task Update(Guid token, User user) { await client.User.Update(token, user); }
            public async Task Update(Guid token, MedicalDevice md) { await client.MedicalDevice.Update(token, md); }

            public async Task Delete(Guid token, bool deleteChildren) { await client.User.Delete(token, deleteChildren); }

            public async Task Delete(MedicalDevice md)
            {
                if (md.SerialNumber == null)
                {
                    System.Diagnostics.Debug.WriteLine("serialnumber null");
                    md.SerialNumber = "";
                }
                await client.MedicalDevice.Delete(md.SerialNumber, md.TypeNumber);
            }

        }


        readonly Lazy<IFinetunerRestApi> background;
        readonly Lazy<IFinetunerRestApi> userInitiated;
        readonly Lazy<IFinetunerRestApi> speculative;
        ObservableQueue<IStorableObject> Queue;



        public static async Task<bool> FinetunerServerReachable()
        {
            bool isConnected = CrossConnectivity.Current.IsConnected;
            bool remoteReachable = await CrossConnectivity.Current.IsRemoteReachable("mobile-app-api.medel.cloud/");
            return isConnected && remoteReachable;

        }



        public RestManager()
        {
            Func<HttpMessageHandler, IFinetunerRestApi> createClient = messageHandler =>
            {
                var client = new FinetunerClient(messageHandler);
                return client;
            };


            Queue = new ObservableQueue<IStorableObject>();
            CrossConnectivity.Current.ConnectivityChanged -= HandleQueue;
            CrossConnectivity.Current.ConnectivityChanged += HandleQueue;

            background = new Lazy<IFinetunerRestApi>(() => createClient(
               new RateLimitedHttpMessageHandler(new NativeMessageHandler(), Priority.Background)));

            userInitiated = new Lazy<IFinetunerRestApi>(() => createClient(
                new RateLimitedHttpMessageHandler(new NativeMessageHandler(), Priority.UserInitiated)));

            speculative = new Lazy<IFinetunerRestApi>(() => createClient(
                new RateLimitedHttpMessageHandler(new NativeMessageHandler(), Priority.Speculative)));

        }


        void HandleQueue(object sender, ConnectivityChangedEventArgs e) 
        {
            if (!e.IsConnected)
            {
                System.Diagnostics.Debug.WriteLine("all the calls will be redirected to the queue");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("....connection restabilished: ........pushing the changes to the server........");
                while (Queue.Count > 0)
                    Queue.Dequeue();   // on dequeue() attach method for remote calls 
            }

        }

        public static RestManager Instance
        {
            get { if (instance == null) instance = new RestManager(); return instance; }
        }
        static RestManager instance;


        public IFinetunerRestApi Background
        {
            get { return background.Value; }
        }

        public IFinetunerRestApi UserInitiated
        {
            get { return userInitiated.Value; }
        }

        public IFinetunerRestApi Speculative
        {
            get { return speculative.Value; }
        }


        public async Task<User> GetRemoteUser(User u) { return await UserInitiated.GetRemoteUser(u.UserToken); }
        public async Task<MedicalDevice> GetRemoteMedicalDevice(MedicalDevice medDev) { return await UserInitiated.GetRemoteMedicalDevice(medDev); }


        public async Task<ILoginResult> Login(string email, string password, string udid, Priority priority)
        {
            System.Diagnostics.Debug.WriteLine($"Attempting to perform the login.......device {udid} {email}");
            ILoginResult result = null;
            Task<ILoginResult> getLoginDataTask;
            switch (priority)
            {
                case Priority.Background:
                    getLoginDataTask = Background.Login(email, password, udid);
                    break;
                case Priority.UserInitiated:
                    getLoginDataTask = UserInitiated.Login(email, password, udid);
                    break;
                case Priority.Speculative:
                    getLoginDataTask = Speculative.Login(email, password, udid);
                    break;
                default:
                    getLoginDataTask = UserInitiated.Login(email, password, udid);
                    break;
            }

            if (await FinetunerServerReachable())
            {
                result = await Policy
                      .Handle<WebException>()
                      .WaitAndRetryAsync
                      (
                        retryCount: 5,
                        sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt))
                      )
                      .ExecuteAsync(async () => await getLoginDataTask);
            }

            return result;
        }



        //public async Task<List<User>> GetUsersAsync(ILoginResult result, Priority priority)
        //{
        //    List<User> users = null;
        //    Task<List<User>> getUsersTask;
        //    switch (priority)
        //    {
        //        case Priority.Background:
        //            getUsersTask = Background.GetRemoteUsers(result);
        //            break;
        //        case Priority.UserInitiated:
        //            getUsersTask = UserInitiated.GetRemoteUsers(result);
        //            break;
        //        case Priority.Speculative:
        //            getUsersTask = Speculative.GetRemoteUsers(result);
        //            break;
        //        default:
        //            getUsersTask = UserInitiated.GetRemoteUsers(result);
        //            break;
        //    }

        //    if (await CheckConnection())
        //    {
        //        users = await Policy
        //              .Handle<WebException>()
        //              .WaitAndRetryAsync
        //              (
        //                retryCount: 5,
        //                sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt))
        //              )
        //              .ExecuteAsync(async () => await getUsersTask);
        //    }
        //    return users;
        //}


        public async Task<List<User>> GetUsersAndMedicalDevicesAsync(ILoginResult result, Priority priority = Priority.UserInitiated)
        {
            List<User> users = null;
            Task<List<User>> getUsersAndDevicesTask;
            switch (priority)
            {
                case Priority.Background:
                    getUsersAndDevicesTask = Background.GetRemoteUsersAndDevices(result);
                    break;
                case Priority.UserInitiated:
                    getUsersAndDevicesTask = UserInitiated.GetRemoteUsersAndDevices(result);
                    break;
                case Priority.Speculative:
                    getUsersAndDevicesTask = Speculative.GetRemoteUsersAndDevices(result);
                    break;
                default:
                    getUsersAndDevicesTask = UserInitiated.GetRemoteUsersAndDevices(result);
                    break;
            }

            if (await FinetunerServerReachable())
            {
                users = await Policy
                      .Handle<WebException>()
                      .WaitAndRetryAsync
                      (
                        retryCount: 5,
                        sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt))
                      )
                      .ExecuteAsync(async () => await getUsersAndDevicesTask);
            }
            return users;
        }

        public async Task<Guid> Create(User user, string udid)
        {
            var create = (user.ParentUserEmail == default(string)) ? UserInitiated.Create(user, udid) : UserInitiated.Create(user, user.ParentGuid);
            Guid guid = default(Guid);
            if (await FinetunerServerReachable())
            {
                guid = await Policy
                      .Handle<WebException>()
                      .WaitAndRetryAsync
                      (
                        retryCount: 5,
                        sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt))
                      )
                      .ExecuteAsync(async () => await create);
            }
            else
            {
                Queue.Enqueue(user);
                System.Diagnostics.Debug.WriteLine(Queue);            
            }
            return guid;
        }


        public async Task<string> Create(MedicalDevice md)
        {
            var create = UserInitiated.Create(md.UserToken, md);
            string dunnowhat = default(string);
            if (await FinetunerServerReachable())
            {
                dunnowhat = await Policy
                      .Handle<WebException>()
                      .WaitAndRetryAsync
                      (
                        retryCount: 5,
                        sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt))
                      )
                      .ExecuteAsync(async () => await create);
            }
            else
            {
                Queue.Enqueue(md);
                System.Diagnostics.Debug.WriteLine(Queue);
            }
            return dunnowhat;
        }


        public async Task<List<MedicalDevice>> GetMedDevices(User user, Priority priority = Priority.UserInitiated)
        {
            List<MedicalDevice> devices = null;
            Task<List<MedicalDevice>> getDevicesTask;
            switch (priority)
            {
                case Priority.Background:
                    getDevicesTask = Background.GetRemoteMedicalDevices(user);
                    break;
                case Priority.UserInitiated:
                    getDevicesTask = UserInitiated.GetRemoteMedicalDevices(user);
                    break;
                case Priority.Speculative:
                    getDevicesTask = Speculative.GetRemoteMedicalDevices(user);
                    break;
                default:
                    getDevicesTask = UserInitiated.GetRemoteMedicalDevices(user);
                    break;
            }

            if (await FinetunerServerReachable())
            {
                devices = await Policy
                      .Handle<WebException>()
                      .WaitAndRetryAsync
                      (
                        retryCount: 5,
                        sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt))
                      )
                      .ExecuteAsync(async () => await getDevicesTask);
            }
           
            return devices;
        }



        public async Task<bool> Update(IStorableObject o)
        {
            bool success = true;
            var update = (o is User) ? UserInitiated.Update((o as User).UserToken, o as User) : UserInitiated.Update((o as MedicalDevice).UserToken, o as MedicalDevice);
            if (await FinetunerServerReachable())
            {
                //returns void - to solve
                 await Policy
                      .Handle<WebException>()
                      .WaitAndRetryAsync
                      (
                        retryCount: 5,
                        sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt))
                      )
                      .ExecuteAsync(async () => await update);
            }
            else
            {
                Queue.Enqueue(o);
                System.Diagnostics.Debug.WriteLine(Queue);
            }
            return success;
        }



        public async Task<bool> Delete(IStorableObject o, bool deleteChildren)
        {
            bool success = true;
            var delete = (o is User) ? UserInitiated.Delete((o as User).UserToken, deleteChildren) : UserInitiated.Delete(o as MedicalDevice);
            if (await FinetunerServerReachable())
            {
                //returns void - to solve
                await Policy
                     .Handle<WebException>()
                     .WaitAndRetryAsync
                     (
                       retryCount: 5,
                       sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt))
                     )
                     .ExecuteAsync(async () => await delete);
            }
            else
            {
                o.ExpirationTime = DateTime.Now;
                Queue.Enqueue(o);
                System.Diagnostics.Debug.WriteLine(Queue);
            }
            return success;
        }




    }





}
