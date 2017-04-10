using Akavache;
using System.Reactive.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Finetuner.Models.Administration;
using Newtonsoft.Json;
using System.Collections;

namespace Finetuner.Services.Storage
{
    /// <summary>
    /// Manages the cache storing a dictionary in it.
    /// Key for USER: 'user #' (main user will be user 0)
    /// Key for DEVICE: 'device #'
    /// Key for GUID: "userToken"
    /// </summary>
    class CacheManager
    {

        IBlobCache cache;
        JsonSerializerSettings settings;

        protected CacheManager()
        {
            cache = BlobCache.LocalMachine;
            settings = new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.Auto };
        }

     

        public static CacheManager Instance
        {
            get { if (instance == null) instance = new CacheManager(); return instance; }
        }
        static CacheManager instance;



     
        public async Task<T> Get<T> (string key)
        {
            T u = default(T);
            try
            {
                string res = await cache.GetObject<string>(key);
                u = (T)JsonConvert.DeserializeObject(res, typeof(T), settings);
            }
            catch (KeyNotFoundException ex)
            {
                Debug.WriteLine(ex.Message);
            }
            return u;
        }


        public async Task<bool> Store(IStorableObject o, string key)
        {
            await cache.InsertObject(key, JsonConvert.SerializeObject(o, o.GetType(), settings));
            Debug.WriteLine("Storing in the cache: {0} ", o);
            return true;
        }


        public async Task<bool> Update(IStorableObject o)
        {
            await cache.InsertObject(o.CacheKey, JsonConvert.SerializeObject(o, o.GetType(), settings));
            Debug.WriteLine("Updating in the cache: {0} ", o);
            return true;
        }


        public async Task<bool> Delete(IStorableObject o)
        {
            await cache.InvalidateObject<IStorableObject>(o.CacheKey);
            Debug.WriteLine("Deleting from the cache: {0} ", o);
            return true;
        }



        public async Task<bool> Clean()
        {
            await cache.Flush();
            await cache.InvalidateAll();
            return true;
        }



        //public static void Flush()
        //{
        //    BlobCache.Shutdown().Wait();
        //}





        public async Task<IEnumerable<User>> GetChildren()
        {
            List<User> list = (List<User>)await cache.GetAllObjects<User>();
            list.Remove(list?.Single(u => u.CacheID == 0));
            if (list.Count() > 1)
                list = list.OrderBy(u => u.CacheID).ToList();
            Debug.WriteLine("I've found " + list.Count() + " managed users");
            return list;
        }


      



    }

}
