using GSwap.Data;
using GSwap.Data.Providers;
using GSwap.Models;
using GSwap.Models.Domain;
using GSwap.Models.Domain.Cooks;
using GSwap.Models.Domain.Users;
using GSwap.Models.Requests.Email;
using GSwap.Models.Requests.Users;
using GSwap.Services.Cryptography;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using System.Web;

namespace GSwap.Services
{
    public class UserService : IUserService
    {
        private IAuthenticationService _authenticationService;
        private ICryptographyService _cryptographyService;
        private IDataProvider _dataProvider;
        private ICacheService _cacheService;
        private const int HASH_ITERATION_COUNT = 1;
        private const int RAND_LENGTH = 15;

        public UserService(IAuthenticationService authSerice, ICryptographyService cryptographyService, IDataProvider dataProvider, ICacheService cacheService)
        {
            _authenticationService = authSerice;
            _dataProvider = dataProvider;
            _cryptographyService = cryptographyService;
            _cacheService = cacheService;
        }

        private int Add(UserAddRequest request, string salt, string passwordHash, string role)
        {
            int userId = 0;
            _dataProvider.ExecuteNonQuery("dbo.Users_Insert"
                , inputParamMapper: delegate (SqlParameterCollection paramCollection)
                {
                    paramCollection.AddWithValue("@FirstName", request.FirstName);
                    paramCollection.AddWithValue("@LastName", request.LastName);
                    paramCollection.AddWithValue("@Email", request.Email);
                    paramCollection.AddWithValue("@Number", request.Number);
                    paramCollection.AddWithValue("@ZipCode", request.ZipCode);
                    paramCollection.AddWithValue("@Salt", salt);
                    paramCollection.AddWithValue("@PasswordHash", passwordHash);
                    paramCollection.AddWithValue("@Role", role);

                    SqlParameter idParameter = new SqlParameter("@Id", SqlDbType.Int);
                    idParameter.Direction = System.Data.ParameterDirection.Output;

                    paramCollection.Add(idParameter);

                }, returnParameters: delegate (SqlParameterCollection param)
                {
                    Int32.TryParse(param["@Id"].Value.ToString(), out userId);
                }
                );

            return userId;

        }



        private void ChangePassword(int userId, string salt, string passwordHash)
        {


            Action<SqlParameterCollection> inputParamDelegate = delegate (SqlParameterCollection paramCollection)
            {
                paramCollection.AddWithValue("@Id", userId);
                paramCollection.AddWithValue("@Salt", salt);
                paramCollection.AddWithValue("@PasswordHash", passwordHash);


            };

            string proc = "dbo.Users_UpdatePasswordById";
            _dataProvider.ExecuteNonQuery(proc, inputParamDelegate);



        }
        public void Update(UserUpdateRequest request)
        {

            _dataProvider.ExecuteNonQuery("dbo.Users_Update"
               , inputParamMapper: delegate (SqlParameterCollection paramCollection)
               {
                   paramCollection.AddWithValue("@Id", request.Id);
                   paramCollection.AddWithValue("@FirstName", request.FirstName);
                   paramCollection.AddWithValue("@LastName", request.LastName);
                   paramCollection.AddWithValue("@Email", request.Email);
                   paramCollection.AddWithValue("@Password", request.Password);
                   paramCollection.AddWithValue("@Number", request.Number);
                   paramCollection.AddWithValue("@ZipCode", request.ZipCode);

               }, returnParameters: delegate (SqlParameterCollection param)
               {

               }
               );


        }
        public bool LogIn(string email, string password)
        {
            bool isSuccessful = false;


            string salt = GetSalt(email);
            if (salt == null)
            {
                return isSuccessful;
            }
            if (!String.IsNullOrEmpty(salt))
            {
                string passwordHash = _cryptographyService.Hash(password, salt, HASH_ITERATION_COUNT);

                IUserAuthData response = Get(email, passwordHash);

                if (response != null)
                {
                    _authenticationService.LogIn(response);               
                    isSuccessful = true;

                }
            }

            return isSuccessful;
        }

        public string GetCookie()
        {
            string cookie = HttpContext.Current.Response.Cookies["authentication"].Value;
            return cookie;
        }

       
        public int Create(UserAddRequest userModel, string role)
        {

            string salt;
            string passwordHash;

            string password = userModel.Password;

            salt = _cryptographyService.GenerateRandomString(RAND_LENGTH);
            passwordHash = _cryptographyService.Hash(password, salt, HASH_ITERATION_COUNT);

            //DB provider call to create user and get us a user id

            //be sure to store both salt and passwordHash
            //DO NOT STORE the original password value that the user passed us

            int userId = Add(userModel, salt, passwordHash, role);
            return userId;
        }

        public void SetNewPassword(ResetRequest request, int userId)
        {

            string salt;
            string passwordHash;

            string cPassword = request.ConfirmPassword;

            salt = _cryptographyService.GenerateRandomString(RAND_LENGTH);
            passwordHash = _cryptographyService.Hash(cPassword, salt, HASH_ITERATION_COUNT);
            ChangePassword(userId, salt, passwordHash);
            //DB provider call to create user and get us a user id

            //be sure to store both salt and passwordHash
            //DO NOT STORE the original password value that the user passed us

            //void userId = Replace(request, salt, passwordHash);

        }



        /// <summary>
        /// Gets the Data call to get a user
        /// </summary>
        /// <param name="email"></param>
        /// <param name="passwordHash"></param>
        /// <returns></returns>
        private IUserAuthData Get(string email, string passwordHash)
        {
            int id = 0;
            string name = null;
            List<string> roles = null;
            Action<SqlParameterCollection> inputParamDelegate = delegate (SqlParameterCollection paramCollection)
            {
                paramCollection.AddWithValue("@Email", email);
                paramCollection.AddWithValue("@PasswordHash", passwordHash);
                //strings have to match the stored proc parameter names
            };

            Action<IDataReader, short> singleRecMapper = delegate (IDataReader reader, short set)
            {
                if (set == 0)
                {
                    int startingIndex = 0; //startingOrdinal

                    id = reader.GetSafeInt32(startingIndex++);
                    name = reader.GetSafeString(startingIndex++);
                }
                else if (set == 1)
                {
                    if (roles == null)
                    {
                        roles = new List<string>();
                    }

                    int startingIndex = 0;
                    roles.Add(reader.GetSafeString(startingIndex++));
                }

                //1.

            };

            _dataProvider.ExecuteCmd("dbo.Users_GetUser", inputParamDelegate, singleRecMapper);


            return new UserBase
            {
                Id = id,
                Name = name,
                //assign roles here
                Roles = roles
            };
        }

        /// <summary>
        /// The Dataprovider call to get the Salt for User with the given UserName/Email
        /// </summary>
        /// <param name="email"></param>
        /// <returns></returns>
        private string GetSalt(string email)
        {
            string salt = null;

            Action<SqlParameterCollection> inputParamDelegate = delegate (SqlParameterCollection paramCollection)
            {
                paramCollection.AddWithValue("@Email", email);

                //output parameter
                SqlParameter saltParameter = new SqlParameter("@Salt", SqlDbType.NVarChar);
                saltParameter.Direction = System.Data.ParameterDirection.Output;
                saltParameter.Size = 100;
                paramCollection.Add(saltParameter);

            };
            Action<SqlParameterCollection> returnParamDelegate = delegate (SqlParameterCollection paramCollection)
            {
                //tell greggy that this fires when email doesn't exist in DB. it says cant convert to string. should i add some logic here?
                if (paramCollection["@Salt"].Value != DBNull.Value)
                {
                    salt = paramCollection["@Salt"].Value.ToString();
                }

            };

            _dataProvider.ExecuteNonQuery("dbo.Users_GetSalt", inputParamDelegate, returnParamDelegate);

            return salt;
        }


        public UserInfo GetUser(int id)
        {
            UserInfo user = null;

            Action<IDataReader, short> singleRecMapper = delegate (IDataReader reader, short set)
            {
                user = new UserInfo();

                int startingIndex = 0;

                user.Id = reader.GetSafeInt32(startingIndex++);
                user.FirstName = reader.GetSafeString(startingIndex++);
                user.LastName = reader.GetSafeString(startingIndex++);
                user.Email = reader.GetSafeString(startingIndex++);
                user.PhoneNumber = reader.GetSafeString(startingIndex++);



            };

            Action<SqlParameterCollection> inputParamDelegate = delegate (SqlParameterCollection paramCollection)
            {
                paramCollection.AddWithValue("@Id", id);
                //strings have to match the stored proc parameter names
            };

            _dataProvider.ExecuteCmd("dbo.Users_SelectById", inputParamDelegate, singleRecMapper);
            return user;

        }
        //Checks if users email is in the system
        public bool UserExists(string email)
        {
            bool exists = false;

            Action<SqlParameterCollection> inputParamDelegate = delegate (SqlParameterCollection paramCollection)
            {

                paramCollection.AddWithValue("@Email", email);

                SqlParameter outputParameter = new SqlParameter("@Exists", System.Data.SqlDbType.Bit);
                outputParameter.Direction = System.Data.ParameterDirection.Output;

                paramCollection.Add(outputParameter);
            };

            Action<SqlParameterCollection> returnParamDelegate = delegate (SqlParameterCollection paramCollection)
            {
                Boolean.TryParse(paramCollection["@Exists"].Value.ToString(), out exists);
                //int.TryParse("@Exists", out exists);
            };

            string proc = "dbo.Users_Exists";
            _dataProvider.ExecuteNonQuery(proc, inputParamDelegate, returnParamDelegate);

            return exists;

        }

        //gets email associated with given Id
        public string GetEmailById(int userId)
        {
            string email = null;

            Action<SqlParameterCollection> inputParamDelegate = delegate (SqlParameterCollection paramCollection)
            {

                paramCollection.AddWithValue("@Id", userId);

                SqlParameter outputParameter = new SqlParameter("@Email", System.Data.SqlDbType.NVarChar, 128);
                outputParameter.Direction = System.Data.ParameterDirection.Output;

                paramCollection.Add(outputParameter);
            };

            Action<SqlParameterCollection> returnParamDelegate = delegate (SqlParameterCollection paramCollection)
            {
                email = paramCollection["@Email"].Value.ToString();

            };

            string proc = "Users_SelectEmailById";
            _dataProvider.ExecuteNonQuery(proc, inputParamDelegate, returnParamDelegate);

            return email;

        }



        

        private Dictionary<int, string> rolesConstant = new Dictionary<int, string>()
        {
            {1, "User"},
            {2, "Chef"},
            {3, "Admin"},
            {4, "Driver"}
        };

        private static User UserMapper(IDataReader reader)
        {
            User user = new User();
            int startingIndex = 0;

            user.Id = reader.GetSafeInt32(startingIndex++);
            user.FirstName = reader.GetSafeString(startingIndex++);
            user.LastName = reader.GetSafeString(startingIndex++);
            user.Email = reader.GetSafeString(startingIndex++);
            user.Number = reader.GetSafeString(startingIndex++);
            user.ZipCode = reader.GetSafeString(startingIndex++);
            user.DateAdded = reader.GetSafeDateTime(startingIndex++);
            user.DateModified = reader.GetSafeDateTime(startingIndex++);
            user.IsConfirmed = reader.GetBoolean(startingIndex++);
            user.Disabled = reader.GetBoolean(startingIndex++);
            return user;
        }

        public PagedList<User> GetPaginatedUsers(PagedUsersRequest request)
        {
            int totalCount = 0;
            PagedList<User> pagedContent = null;
            List<User> pagedUsers = null;
            Dictionary<int, List<string>> userRolesDict = null;

            Action<SqlParameterCollection> inputParamDelegate = delegate (SqlParameterCollection paramCollection)
            {

                paramCollection.AddWithValue("@PageIndex", request.PageIndex);
                paramCollection.AddWithValue("@PageSize", request.PageSize);
                paramCollection.AddWithValue("@SortTypeId", request.SortTypeId);
                paramCollection.AddWithValue("@SearchTerm", request.SearchTerm);
                paramCollection.AddWithValue("@RoleId", request.RoleId);
            };

            Action<IDataReader, short> singleRecMapper = delegate (IDataReader reader, short set)
            {

                if (set == 0)
                {

                    int startingIndex = 0;
                    int userId = reader.GetSafeInt32(startingIndex++);
                    int roleId = reader.GetSafeInt32(startingIndex++);

                    if (userRolesDict == null)
                    {
                        totalCount = reader.GetSafeInt32(startingIndex++);
                        userRolesDict = new Dictionary<int, List<string>>();
                    }

                    if (!userRolesDict.ContainsKey(userId))
                    {
                        userRolesDict.Add(userId, new List<string>());
                    }

                    userRolesDict[userId].Add(rolesConstant[roleId]);

                }

                if (set == 1)
                {

                    User user = UserMapper(reader);

                    if (pagedUsers == null)
                    {
                        pagedUsers = new List<User>();
                    }

                    pagedUsers.Add(user);

                }
            };

            _dataProvider.ExecuteCmd("dbo.Users_Pagination", inputParamDelegate, singleRecMapper);

            if (pagedUsers != null && userRolesDict != null)
            {
                foreach (User currentUser in pagedUsers)
                {
                    if (userRolesDict.ContainsKey(currentUser.Id))
                    {
                        currentUser.Roles = userRolesDict[currentUser.Id];
                    }
                }
            }

            if (pagedContent == null)
            {
                pagedContent = new PagedList<User>(pagedUsers, request.PageIndex, request.PageSize, totalCount);
            }

            if(pagedContent != null && pagedContent.TotalCount == 0)
            {
                return null;
            }

            return pagedContent;

        }
        public void ChangeUserDisableStatus(UpdateDisableStatusRequest model)
        {

            Action<SqlParameterCollection> inputParamDelegate = delegate (SqlParameterCollection paramCollection)
            {
                paramCollection.AddWithValue("@ChangeStatus", model.Disabled);
                paramCollection.AddWithValue("@Id", model.Id);
            };

            _dataProvider.ExecuteNonQuery("dbo.Users_EnableDisableUser", inputParamDelegate);
        }

    }
}
