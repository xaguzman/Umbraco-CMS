using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Web;
using System.Web.Caching;
using System.Xml;

using umbraco.cms.businesslogic.cache;

using umbraco.BusinessLogic;
using umbraco.DataLayer;

using System.Web.Security;
using System.Text;
using System.Security.Cryptography;

namespace umbraco.cms.businesslogic.member
{
    /// <summary>
    /// The Member class represents a member of the public website (not to be confused with umbraco users)
    /// 
    /// Members are used when creating communities and collaborative applications using umbraco, or if there are a 
    /// need for identifying or authentifying the visitor. (extranets, protected/private areas of the public website)
    /// 
    /// Inherits generic datafields from it's baseclass content.
    /// </summary>
    public class Member : Content
    {
        public static readonly string UmbracoMemberProviderName = "UmbracoMembershipProvider";
        public static readonly string UmbracoRoleProviderName = "UmbracoRoleProvider";
        public static readonly Guid _objectType = new Guid("39eb0f98-b348-42a1-8662-e7eb18487560");
        private static readonly System.Web.Caching.Cache _memberCache = HttpRuntime.Cache;
        private static object memberCacheSyncLock = new object();
        private static readonly string memberLookupCacheKey = "memberLookupId_";
        private static readonly string UmbracoMemberIdCookieKey = "umbracoMemberId";
        private static readonly string UmbracoMemberGuidCookieKey = "umbracoMemberGuid";
        private static readonly string UmbracoMemberLoginCookieKey = "umbracoMemberLogin";
        private string _text;

        private Hashtable _groups = null;

        /// <summary>
        /// Initializes a new instance of the Member class.
        /// </summary>
        /// <param name="id">Identifier</param>
        public Member(int id)
            : base(id)
        {
        }

        /// <summary>
        /// Initializes a new instance of the Member class.
        /// </summary>
        /// <param name="id">Identifier</param>
        public Member(Guid id)
            : base(id)
        {
        }

        /// <summary>
        /// Initializes a new instance of the Member class, with an option to only initialize 
        /// the data used by the tree in the umbraco console.
        /// 
        /// Performace
        /// </summary>
        /// <param name="id">Identifier</param>
        /// <param name="noSetup"></param>
        public Member(int id, bool noSetup)
            : base(id, noSetup)
        {
        }

        /// <summary>
        /// The name of the member
        /// </summary>
        public new string Text
        {
            get
            {
                if (string.IsNullOrEmpty(_text))
                    _text = SqlHelper.ExecuteScalar<string>(
                        "select text from umbracoNode where id = @id",
                        SqlHelper.CreateParameter("@id", Id));
                return _text;
            }
            set
            {
                _text = value;
                base.Text = value;
            }
        }

        /// <summary>
        /// A list of all members in the current umbraco install
        /// 
        /// Note: is ressource intensive, use with care.
        /// </summary>
        public static Member[] GetAll
        {
            get
            {
                Guid[] tmp = getAllUniquesFromObjectType(_objectType);

                return Array.ConvertAll<Guid, Member>(tmp, delegate(Guid g) { return new Member(g); });
            }
        }

        /// <summary>
        /// The members password, used when logging in on the public website
        /// </summary>
        public string Password
        {
            get
            {
                return SqlHelper.ExecuteScalar<string>(
                    "select Password from cmsMember where nodeId = @id",
                    SqlHelper.CreateParameter("@id", Id));
            }
            set
            {
                // We need to use the provider for this in order for hashing, etc. support
                // To write directly to the db use the ChangePassword method
                // this is not pretty but nessecary due to a design flaw (the membership provider should have been a part of the cms project)
                MemberShipHelper helper = new MemberShipHelper();
                ChangePassword(helper.EncodePassword(value, Membership.Provider.PasswordFormat));
            }
        }

        /// <summary>
        /// The loginname of the member, used when logging in
        /// </summary>
        public string LoginName
        {
            get
            {
                return SqlHelper.ExecuteScalar<string>(
                    "select LoginName from cmsMember where nodeId = @id",
                    SqlHelper.CreateParameter("@id", Id));
            }
            set
            {
                SqlHelper.ExecuteNonQuery(
                    "update cmsMember set LoginName = @loginName where nodeId =  @id",
                    SqlHelper.CreateParameter("@loginName", value),
                    SqlHelper.CreateParameter("@id", Id));
            }
        }

        /// <summary>
        /// A list of groups the member are member of
        /// </summary>
        public Hashtable Groups
        {
            get
            {
                if (_groups == null)
                    populateGroups();
                return _groups;
            }
        }

        /// <summary>
        /// The members email
        /// </summary>
        public string Email
        {
            get
            {
                return SqlHelper.ExecuteScalar<string>(
                    "select Email from cmsMember where nodeId = @id",
                    SqlHelper.CreateParameter("@id", Id));
            }
            set
            {
                SqlHelper.ExecuteNonQuery(
                    "update cmsMember set Email = @email where nodeId = @id",
                    SqlHelper.CreateParameter("@id", Id), SqlHelper.CreateParameter("@email", value));
            }
        }

        /// <summary>
        /// Used to persist object changes to the database. In Version3.0 it's just a stub for future compatibility
        /// </summary>
        public override void Save()
        {
            SaveEventArgs e = new SaveEventArgs();
            FireBeforeSave(e);

            if (!e.Cancel)
            {
                // re-generate xml
                XmlGenerate(new XmlDocument());

                FireAfterSave(e);
            }
        }

        /// <summary>
        /// Retrieves a list of members thats not start with a-z
        /// </summary>
        /// <returns>array of members</returns>
        public static Member[] getAllOtherMembers()
        {
            string query =
                "SELECT id, text FROM umbracoNode WHERE (nodeObjectType = @nodeObjectType) AND (ASCII(SUBSTRING(text, 1, 1)) NOT BETWEEN ASCII('a') AND ASCII('z')) AND (ASCII(SUBSTRING(text, 1, 1)) NOT BETWEEN ASCII('A') AND ASCII('Z'))";
            List<Member> m = new List<Member>();
            using (IRecordsReader dr = SqlHelper.ExecuteReader(query,
                SqlHelper.CreateParameter("@nodeObjectType", _objectType)))
            {
                while (dr.Read())
                {
                    Member newMember = new Member(dr.GetInt("id"), true);
                    newMember._text = dr.GetString("text");
                    m.Add(new Member(newMember.Id));
                }
            }

            return m.ToArray();
        }

        /// <summary>
        /// Retrieves a list of members by the first letter in their name.
        /// </summary>
        /// <param name="letter">The first letter</param>
        /// <returns></returns>
        public static Member[] getMemberFromFirstLetter(char letter)
        {
            return GetMemberByName(letter.ToString(), true);
        }

        public static Member[] GetMemberByName(string usernameToMatch, bool matchByNameInsteadOfLogin)
        {
            string field = matchByNameInsteadOfLogin ? "text" : "loginName";
            string query =
                String.Format(
                "Select id, text from umbracoNode inner join cmsMember on cmsMember.nodeId = umbracoNode.id where nodeObjectType = @objectType and {0} like @letter order by text",
                field);
            List<Member> m = new List<Member>();
            using (IRecordsReader dr = SqlHelper.ExecuteReader(query,
                SqlHelper.CreateParameter("@objectType", _objectType),
                SqlHelper.CreateParameter("@field", field),
                SqlHelper.CreateParameter("@letter", usernameToMatch + "%")))
            {
                while (dr.Read())
                {
                    Member newMember = new Member(dr.GetInt("id"), true);
                    newMember._text = dr.GetString("text");
                    m.Add(new Member(newMember.Id));
                }
            }
            return m.ToArray();

        }

        /// <summary>
        /// Creates a new member
        /// </summary>
        /// <param name="Name">Membername</param>
        /// <param name="mbt">Member type</param>
        /// <param name="u">The umbraco usercontext</param>
        /// <returns>The new member</returns>
        public static Member MakeNew(string Name, MemberType mbt, User u)
        {
            return MakeNew(Name, "", mbt, u);
            /*
            Guid newId = Guid.NewGuid();
            MakeNew(-1, _objectType, u.Id, 1, Name, newId);

            Member tmp = new Member(newId);

            tmp.CreateContent(mbt);
            // Create member specific data ..
            SqlHelper.ExecuteNonQuery(
                "insert into cmsMember (nodeId,Email,LoginName,Password) values (@id,'',@text,'')",
                SqlHelper.CreateParameter("@id", tmp.Id),
                SqlHelper.CreateParameter("@text", tmp.Text));

            NewEventArgs e = new NewEventArgs();
            tmp.OnNew(e);

            return tmp;*/
        }

        /// <summary>
        /// Creates a new member
        /// </summary>
        /// <param name="Name">Membername</param>
        /// <param name="mbt">Member type</param>
        /// <param name="u">The umbraco usercontext</param>
        /// <param name="Email">The email of the user</param>
        /// <returns>The new member</returns>
        public static Member MakeNew(string Name, string Email, MemberType mbt, User u)
        {
            // Test for e-mail
            if (Email != "" && Member.GetMemberFromEmail(Email) != null)
                throw new Exception(String.Format("Duplicate Email! A member with the e-mail {0} already exists", Email));
            else if (Member.GetMemberFromLoginName(Name) != null)
                throw new Exception(String.Format("Duplicate User name! A member with the user name {0} already exists", Name));

            Guid newId = Guid.NewGuid();
            MakeNew(-1, _objectType, u.Id, 1, Name, newId);

            Member tmp = new Member(newId);

            tmp.CreateContent(mbt);
            // Create member specific data ..
            SqlHelper.ExecuteNonQuery(
                "insert into cmsMember (nodeId,Email,LoginName,Password) values (@id,@email,@text,'')",
                SqlHelper.CreateParameter("@id", tmp.Id),
                SqlHelper.CreateParameter("@text", tmp.Text),
                SqlHelper.CreateParameter("@email", Email));

            NewEventArgs e = new NewEventArgs();
            tmp.OnNew(e);

            tmp.Save();

            return tmp;
        }

        /// <summary>
        /// Generates the xmlrepresentation of a member
        /// </summary>
        /// <param name="xd"></param>
        public override void XmlGenerate(XmlDocument xd)
        {
            XmlNode node = xd.CreateNode(XmlNodeType.Element, "node", "");
            XmlPopulate(xd, ref node, false);
            node.Attributes.Append(xmlHelper.addAttribute(xd, "loginName", LoginName));
            node.Attributes.Append(xmlHelper.addAttribute(xd, "email", Email));
            SaveXmlDocument(node);
        }

        /// <summary>
        /// Xmlrepresentation of a member
        /// </summary>
        /// <param name="xd">The xmldocument context</param>
        /// <param name="Deep">Recursive - should always be set to false</param>
        /// <returns>A the xmlrepresentation of the current member</returns>
        public override XmlNode ToXml(XmlDocument xd, bool Deep)
        {
            XmlNode x = base.ToXml(xd, Deep);
            if (x.Attributes["loginName"] == null)
            {
                x.Attributes.Append(xmlHelper.addAttribute(xd, "loginName", LoginName));
                x.Attributes.Append(xmlHelper.addAttribute(xd, "email", Email));
            }
            return x;
        }

        /// <summary>
        /// Deltes the current member
        /// </summary>
        public new void delete()
        {
            DeleteEventArgs e = new DeleteEventArgs();
            FireBeforeDelete(e);

            if (!e.Cancel)
            {
                // Remove from cache (if exists)
                umbraco.cms.businesslogic.cache.Cache.ClearCacheItem(memberLookupCacheKey + Id);

                // delete memeberspecific data!
                SqlHelper.ExecuteNonQuery("Delete from cmsMember where nodeId = @id",
                    SqlHelper.CreateParameter("@id", Id));

                // delete all relations to groups
                foreach (int groupId in this.Groups.Keys)
                {
                    RemoveGroup(groupId);
                }

                // Delete all content and cmsnode specific data!
                base.delete();

                FireAfterDelete(e);
            }
        }

        /// <summary>
        /// Deletes all members of the membertype specified
        /// 
        /// Used when a membertype is deleted
        /// 
        /// Use with care
        /// </summary>
        /// <param name="dt">The membertype which are being deleted</param>
        public static void DeleteFromType(MemberType dt)
        {
            foreach (Content c in getContentOfContentType(dt))
            {
                // due to recursive structure document might already been deleted..
                if (IsNode(c.UniqueId))
                {
                    Member tmp = new Member(c.UniqueId);
                    tmp.delete();
                }
            }
        }

        public void ChangePassword(string newPassword)
        {
            SqlHelper.ExecuteNonQuery(
                    "update cmsMember set Password = @password where nodeId = @id",
                    SqlHelper.CreateParameter("@password", newPassword),
                    SqlHelper.CreateParameter("@id", Id));
        }

        /// <summary>
        /// Adds the member to group with the specified id
        /// </summary>
        /// <param name="GroupId">The id of the group which the member is being added to</param>
        [MethodImpl(MethodImplOptions.Synchronized)]
        public void AddGroup(int GroupId)
        {
            AddGroupEventArgs e = new AddGroupEventArgs();
            FireBeforeAddGroup(e);

            if (!e.Cancel)
            {
                IParameter[] parameters = new IParameter[] { SqlHelper.CreateParameter("@id", Id),
                                                         SqlHelper.CreateParameter("@groupId", GroupId) };
                bool exists = SqlHelper.ExecuteScalar<int>("SELECT COUNT(member) FROM cmsMember2MemberGroup WHERE member = @id AND memberGroup = @groupId",
                                                           parameters) > 0;
                if (!exists)
                    SqlHelper.ExecuteNonQuery("INSERT INTO cmsMember2MemberGroup (member, memberGroup) values (@id, @groupId)",
                                              parameters);
                populateGroups();

                FireAfterAddGroup(e);
            }
        }

        /// <summary>
        /// Removes the member from the MemberGroup specified
        /// </summary>
        /// <param name="GroupId">The MemberGroup from which the Member is removed</param>
        public void RemoveGroup(int GroupId)
        {
            RemoveGroupEventArgs e = new RemoveGroupEventArgs();
            FireBeforeRemoveGroup(e);

            if (!e.Cancel)
            {
                SqlHelper.ExecuteNonQuery(
                    "delete from cmsMember2MemberGroup where member = @id and Membergroup = @groupId",
                    SqlHelper.CreateParameter("@id", Id), SqlHelper.CreateParameter("@groupId", GroupId));
                populateGroups();
                FireAfterRemoveGroup(e);
            }
        }

        private void populateGroups()
        {
            Hashtable temp = new Hashtable();
            using (IRecordsReader dr = SqlHelper.ExecuteReader(
                "select memberGroup from cmsMember2MemberGroup where member = @id",
                SqlHelper.CreateParameter("@id", Id)))
            {
                while (dr.Read())
                    temp.Add(dr.GetInt("memberGroup"),
                        new MemberGroup(dr.GetInt("memberGroup")));
            }
            _groups = temp;
        }

        /// <summary>
        /// Retrieve a member given the loginname
        /// 
        /// Used when authentifying the Member
        /// </summary>
        /// <param name="loginName">The unique Loginname</param>
        /// <returns>The member with the specified loginname - null if no Member with the login exists</returns>
        public static Member GetMemberFromLoginName(string loginName)
        {
            if (IsMember(loginName))
            {
                object o = SqlHelper.ExecuteScalar<object>(
                    "select nodeID from cmsMember where LoginName = @loginName",
                    SqlHelper.CreateParameter("@loginName", loginName));

                if (o == null)
                    return null;

                int tmpId;
                if (!int.TryParse(o.ToString(), out tmpId))
                    return null;

                return new Member(tmpId);
            }
            else
                HttpContext.Current.Trace.Warn("No member with loginname: " + loginName + " Exists");

            return null;
        }

        /// <summary>
        /// Retrieve a Member given an email
        /// 
        /// Used when authentifying the Member
        /// </summary>
        /// <param name="email">The email of the member</param>
        /// <returns>The member with the specified email - null if no Member with the email exists</returns>
        public static Member GetMemberFromEmail(string email)
        {
            if (string.IsNullOrEmpty(email))
                return null;

            object o = SqlHelper.ExecuteScalar<object>(
                "select nodeID from cmsMember where Email = @email",
                SqlHelper.CreateParameter("@email", email));

            if (o == null)
                return null;

            int tmpId;
            if (!int.TryParse(o.ToString(), out tmpId))
                return null;

            return new Member(tmpId);
        }

        /// <summary>
        /// Retrieve a Member given the credentials
        /// 
        /// Used when authentifying the member
        /// </summary>
        /// <param name="loginName">Member login</param>
        /// <param name="password">Member password</param>
        /// <returns>The member with the credentials - null if none exists</returns>
        public static Member GetMemberFromLoginNameAndPassword(string loginName, string password)
        {
            if (IsMember(loginName))
            {
                // validate user via provider
                if (Membership.ValidateUser(loginName, password))
                {
                    return GetMemberFromLoginName(loginName);
                }
                else
                {
                    HttpContext.Current.Trace.Warn("Incorrect login/password");
                    return null;
                }
            }
            else
            {
                HttpContext.Current.Trace.Warn("No member with loginname: " + loginName + " Exists");
                //				throw new ArgumentException("No member with Loginname: " + LoginName + " exists");
                return null;
            }
        }

        public static Member GetMemberFromLoginAndEncodedPassword(string loginName, string password)
        {
            object o = SqlHelper.ExecuteScalar<object>(
                "select nodeID from cmsMember where LoginName = @loginName and Password = @password",
                SqlHelper.CreateParameter("loginName", loginName),
                SqlHelper.CreateParameter("password", password));

            if (o == null)
                return null;

            int tmpId;
            if (!int.TryParse(o.ToString(), out tmpId))
                return null;

            return new Member(tmpId);
        }

        public static bool InUmbracoMemberMode()
        {
            return Membership.Provider.Name == UmbracoMemberProviderName;
        }

        public static bool IsUsingUmbracoRoles()
        {
            return Roles.Provider.Name == UmbracoRoleProviderName;
        }


        /// <summary>
        /// Helper method - checks if a Member with the LoginName exists
        /// </summary>
        /// <param name="loginName">Member login</param>
        /// <returns>True if the member exists</returns>
        public static bool IsMember(string loginName)
        {
            Debug.Assert(loginName != null, "loginName cannot be null");
            object o = SqlHelper.ExecuteScalar<object>(
                "select count(nodeID) as tmp from cmsMember where LoginName = @loginName",
                SqlHelper.CreateParameter("@loginName", loginName));
            if (o == null)
                return false;
            int count;
            if (!int.TryParse(o.ToString(), out count))
                return false;
            return count > 0;
        }

        /*
        public contentitem.ContentItem[] CreatedContent() {
            return new contentitem.ContentItem[0];
        }
        */



        #region MemberHandle functions

        /// <summary>
        /// Method is used when logging a member in.
        /// 
        /// Adds the member to the cache of logged in members
        /// 
        /// Uses cookiebased recognition
        /// 
        /// Can be used in the runtime
        /// </summary>
        /// <param name="m">The member to log in</param>
        public static void AddMemberToCache(Member m)
        {

            if (m != null)
            {
                AddToCacheEventArgs e = new AddToCacheEventArgs();
                m.FireBeforeAddToCache(e);

                if (!e.Cancel)
                {

                    Hashtable umbracoMembers = CachedMembers();

                    // Check if member already exists
                    if (umbracoMembers[m.Id] == null)
                        umbracoMembers.Add(m.Id, m);

                    removeCookie("umbracoMemberId");

                    // Add cookie with member-id, guid and loginname
                    addCookie("umbracoMemberId", m.Id.ToString(), 365);
                    addCookie("umbracoMemberGuid", m.UniqueId.ToString(), 365);
                    addCookie("umbracoMemberLogin", m.LoginName, 365);

                    // Debug information
                    HttpContext.Current.Trace.Write("member",
                        "Member added to cache: " + m.Text + "/" + m.LoginName + " (" +
                        m.Id + ")");

                    _memberCache["umbracoMembers"] = umbracoMembers;

                    FormsAuthentication.SetAuthCookie(m.LoginName, true);

                    m.FireAfterAddToCache(e);
                }
            }

        }



        #region cookieHelperMethods

        private static void removeCookie(string Name)
        {
            HttpCookie c = HttpContext.Current.Request.Cookies[Name];
            if (c != null)
            {
                c.Expires = DateTime.Now.AddDays(-1);
                HttpContext.Current.Response.Cookies.Add(c);
            }
        }

        private static void addCookie(string Name, object Value, int NumberOfDaysToLast)
        {
            HttpCookie c = new HttpCookie(Name, Value.ToString());
            c.Value = Value.ToString();
            c.Expires = DateTime.Now.AddDays(NumberOfDaysToLast);
            HttpContext.Current.Response.Cookies.Add(c);
        }

        private static void addCookie(string Name, object Value, TimeSpan timeout)
        {
            HttpCookie c = new HttpCookie(Name, Value.ToString());
            c.Value = Value.ToString();
            c.Expires = DateTime.Now.Add(timeout);
            HttpContext.Current.Response.Cookies.Add(c);
        }

        private static string getCookieValue(string Name)
        {
            string tempValue = "";

            if (HttpContext.Current.Session[Name] != null)
                if (HttpContext.Current.Session[Name].ToString() != "0")
                    tempValue = HttpContext.Current.Session[Name].ToString();

            if (tempValue == "")
            {
                if (Array.IndexOf(HttpContext.Current.Response.Cookies.AllKeys, Name) == -1)
                {
                    if (HttpContext.Current.Request.Cookies[Name] != null)
                        if (HttpContext.Current.Request.Cookies[Name].Value != "")
                        {
                            tempValue = HttpContext.Current.Request.Cookies[Name].Value;
                        }
                }
                else
                {
                    tempValue = HttpContext.Current.Response.Cookies[Name].Value;
                }
            }

            return tempValue;
        }

        #endregion

        /// <summary>
        /// Method is used when logging a member in.
        /// 
        /// Adds the member to the cache of logged in members
        /// 
        /// Uses cookie or session based recognition
        /// 
        /// Can be used in the runtime
        /// </summary>
        /// <param name="m">The member to log in</param>
        /// <param name="UseSession">Use sessionbased recognition</param>
        /// <param name="TimespanForCookie">The live time of the cookie</param>
        public static void AddMemberToCache(Member m, bool UseSession, TimeSpan TimespanForCookie)
        {
            if (m != null)
            {
                AddToCacheEventArgs e = new AddToCacheEventArgs();
                m.FireBeforeAddToCache(e);

                if (!e.Cancel)
                {


                    Hashtable umbracoMembers = CachedMembers();

                    // Check if member already exists
                    if (umbracoMembers[m.Id] == null)
                        umbracoMembers.Add(m.Id, m);

                    if (!UseSession)
                    {
                        removeCookie("umbracoMemberId");

                        // Add cookie with member-id
                        addCookie("umbracoMemberId", m.Id.ToString(), TimespanForCookie);
                        addCookie("umbracoMemberGuid", m.UniqueId.ToString(), TimespanForCookie);
                        addCookie("umbracoMemberLogin", m.LoginName, TimespanForCookie);
                    }
                    else
                    {
                        HttpContext.Current.Session["umbracoMemberId"] = m.Id.ToString();
                        HttpContext.Current.Session["umbracoMemberGuid"] = m.UniqueId.ToString();
                        HttpContext.Current.Session["umbracoMemberLogin"] = m.LoginName;
                    }

                    // Debug information
                    HttpContext.Current.Trace.Write("member",
                        string.Format("Member added to cache: {0}/{1} ({2})",
                            m.Text, m.LoginName, m.Id));

                    _memberCache["umbracoMembers"] = umbracoMembers;


                    FormsAuthentication.SetAuthCookie(m.LoginName, false);

                    m.FireAfterAddToCache(e);
                }

            }
        }

        /// <summary>
        /// Removes the member from the cache
        /// 
        /// Can be used in the public website
        /// </summary>
        /// <param name="m">Member to remove</param>
        [Obsolete("Deprecated, use the RemoveMemberFromCache(int NodeId) instead", false)]
        public static void RemoveMemberFromCache(Member m)
        {
            RemoveMemberFromCache(m.Id);
        }

        /// <summary>
        /// Removes the member from the cache
        /// 
        /// Can be used in the public website
        /// </summary>
        /// <param name="NodeId">Node Id of the member to remove</param>
        public static void RemoveMemberFromCache(int NodeId)
        {
            Hashtable umbracoMembers = CachedMembers();
            if (umbracoMembers.ContainsKey(NodeId))
                umbracoMembers.Remove(NodeId);

            _memberCache["umbracoMembers"] = umbracoMembers;
        }

        /// <summary>
        /// Deletes the member cookie from the browser 
        /// 
        /// Can be used in the public website
        /// </summary>
        /// <param name="m">Member</param>
        [Obsolete("Deprecated, use the ClearMemberFromClient(int NodeId) instead", false)]
        public static void ClearMemberFromClient(Member m)
        {

            if (m != null)
                ClearMemberFromClient(m.Id);
            else
            {
                // If the member doesn't exists as an object, we'll just make sure that cookies are cleared
                removeCookie("umbracoMemberId");
                removeCookie("umbracoMemberGuid");
                removeCookie("umbracoMemberLogin");
            }

            FormsAuthentication.SignOut();
        }

        /// <summary>
        /// Deletes the member cookie from the browser 
        /// 
        /// Can be used in the public website
        /// </summary>
        /// <param name="NodeId">The Node id of the member to clear</param>
        public static void ClearMemberFromClient(int NodeId)
        {

            removeCookie("umbracoMemberId");
            removeCookie("umbracoMemberGuid");
            removeCookie("umbracoMemberLogin");

            RemoveMemberFromCache(NodeId);


            FormsAuthentication.SignOut();
        }

        /// <summary>
        /// Retrieve a collection of members in the cache
        /// 
        /// Can be used from the public website
        /// </summary>
        /// <returns>A collection of cached members</returns>
        public static Hashtable CachedMembers()
        {
            Hashtable umbracoMembers;

            // Check for member hashtable in cache
            if (_memberCache["umbracoMembers"] == null)
                umbracoMembers = new Hashtable();
            else
                umbracoMembers = (Hashtable)_memberCache["umbracoMembers"];

            return umbracoMembers;
        }

        /// <summary>
        /// Retrieve a member from the cache
        /// 
        /// Can be used from the public website
        /// </summary>
        /// <param name="id">Id of the member</param>
        /// <returns>If the member is cached it returns the member - else null</returns>
        public static Member GetMemberFromCache(int id)
        {
            Hashtable members = CachedMembers();
            if (members.ContainsKey(id))
                return (Member)members[id];
            else
                return null;
        }

        /// <summary>
        /// An indication if the current visitor is logged in
        /// 
        /// Can be used from the public website
        /// </summary>
        /// <returns>True if the the current visitor is logged in</returns>
        public static bool IsLoggedOn()
        {
            if (HttpContext.Current.User == null)
                return false;


            //if member is not auth'd , but still might have a umb cookie saying otherwise...
            if (!HttpContext.Current.User.Identity.IsAuthenticated)
            {
                int _currentMemberId = CurrentMemberId();

                //if we have a cookie... 
                if (_currentMemberId > 0)
                {
                    //log in the member so .net knows about the member.. 
                    FormsAuthentication.SetAuthCookie(new Member(_currentMemberId).LoginName, true);

                    //making sure that the correct status is returned first time around...
                    return true;
                }

            }


            return HttpContext.Current.User.Identity.IsAuthenticated;
        }


        /// <summary>
        /// Make a lookup in the database to verify if a member truely exists
        /// </summary>
        /// <param name="NodeId">The node id of the member</param>
        /// <returns>True is a record exists in db</returns>
        private static bool memberExists(int NodeId)
        {
            return SqlHelper.ExecuteScalar<int>("select count(nodeId) from cmsMember where nodeId = @nodeId", SqlHelper.CreateParameter("@nodeId", NodeId)) == 1;
        }


        /// <summary>
        /// Gets the current visitors memberid
        /// </summary>
        /// <returns>The current visitors members id, if the visitor is not logged in it returns 0</returns>
        public static int CurrentMemberId()
        {
            int _currentMemberId = 0;
            string _currentGuid = "";

            // For backwards compatibility between umbraco members and .net membership
            if (HttpContext.Current.User.Identity.IsAuthenticated)
            {
                int.TryParse(Membership.GetUser().ProviderUserKey.ToString(), out _currentMemberId);
            }
            else if (StateHelper.HasCookieValue(UmbracoMemberIdCookieKey) &&
               StateHelper.HasCookieValue(UmbracoMemberGuidCookieKey) &&
               StateHelper.HasCookieValue(UmbracoMemberLoginCookieKey))
            {
                int.TryParse(StateHelper.GetCookieValue(UmbracoMemberIdCookieKey), out _currentMemberId);
                _currentGuid = StateHelper.GetCookieValue(UmbracoMemberGuidCookieKey);
            }

            if (_currentMemberId > 0 && !memberExists(_currentMemberId))
            {
                _currentMemberId = 0;

                StateHelper.ClearCookie(UmbracoMemberGuidCookieKey);
                StateHelper.ClearCookie(UmbracoMemberLoginCookieKey);
                StateHelper.ClearCookie(UmbracoMemberIdCookieKey);

            }

            return _currentMemberId;
        }

        /// <summary>
        /// Get the current member
        /// </summary>
        /// <returns>Returns the member, if visitor is not logged in: null</returns>
        public static Member GetCurrentMember()
        {
            try
            {
                int _currentMemberId = CurrentMemberId();
                if (_currentMemberId != 0)
                {
                    // return member from cache
                    Member m = GetMemberFromCache(_currentMemberId);
                    if (m == null)
                        m = new Member(_currentMemberId);

                    if (HttpContext.Current.User.Identity.IsAuthenticated || (m.UniqueId == new Guid(getCookieValue("umbracoMemberGuid")) &&
                       m.LoginName == getCookieValue("umbracoMemberLogin")))
                        return m;

                    return null;
                }
                else
                    return null;
            }
            catch
            {
                return null;
            }
        }

        #endregion


        //EVENTS
        /// <summary>
        /// The save event handler
        /// </summary>
        new public delegate void SaveEventHandler(Member sender, SaveEventArgs e);

        /// <summary>
        /// The new event handler
        /// </summary>
        new public delegate void NewEventHandler(Member sender, NewEventArgs e);

        /// <summary>
        /// The delete event handler
        /// </summary>
        new public delegate void DeleteEventHandler(Member sender, DeleteEventArgs e);

        /// <summary>
        /// The add to cache event handler
        /// </summary>
        public delegate void AddingToCacheEventHandler(Member sender, AddToCacheEventArgs e);

        /// <summary>
        /// The add group event handler
        /// </summary>
        public delegate void AddingGroupEventHandler(Member sender, AddGroupEventArgs e);

        /// <summary>
        /// The remove group event handler
        /// </summary>
        public delegate void RemovingGroupEventHandler(Member sender, RemoveGroupEventArgs e);


        /// <summary>
        /// Occurs when [before save].
        /// </summary>
        new public static event SaveEventHandler BeforeSave;
        /// <summary>
        /// Raises the <see cref="E:BeforeSave"/> event.
        /// </summary>
        /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
        new protected virtual void FireBeforeSave(SaveEventArgs e)
        {
            if (BeforeSave != null)
            {
                BeforeSave(this, e);
            }
        }


        new public static event SaveEventHandler AfterSave;
        new protected virtual void FireAfterSave(SaveEventArgs e)
        {
            if (AfterSave != null)
            {
                AfterSave(this, e);
            }
        }


        new public static event NewEventHandler New;
        new protected virtual void OnNew(NewEventArgs e)
        {
            if (New != null)
            {
                New(this, e);
            }
        }


        public static event AddingGroupEventHandler BeforeAddGroup;
        protected virtual void FireBeforeAddGroup(AddGroupEventArgs e)
        {
            if (BeforeAddGroup != null)
            {
                BeforeAddGroup(this, e);
            }
        }
        public static event AddingGroupEventHandler AfterAddGroup;
        protected virtual void FireAfterAddGroup(AddGroupEventArgs e)
        {
            if (AfterAddGroup != null)
            {
                AfterAddGroup(this, e);
            }
        }


        public static event RemovingGroupEventHandler BeforeRemoveGroup;
        protected virtual void FireBeforeRemoveGroup(RemoveGroupEventArgs e)
        {
            if (BeforeRemoveGroup != null)
            {
                BeforeRemoveGroup(this, e);
            }
        }

        public static event RemovingGroupEventHandler AfterRemoveGroup;
        protected virtual void FireAfterRemoveGroup(RemoveGroupEventArgs e)
        {
            if (AfterRemoveGroup != null)
            {
                AfterRemoveGroup(this, e);
            }
        }


        public static event AddingToCacheEventHandler BeforeAddToCache;
        protected virtual void FireBeforeAddToCache(AddToCacheEventArgs e)
        {
            if (BeforeAddToCache != null)
            {
                BeforeAddToCache(this, e);
            }
        }


        public static event AddingToCacheEventHandler AfterAddToCache;
        protected virtual void FireAfterAddToCache(AddToCacheEventArgs e)
        {
            if (AfterAddToCache != null)
            {
                AfterAddToCache(this, e);
            }
        }

        new public static event DeleteEventHandler BeforeDelete;
        new protected virtual void FireBeforeDelete(DeleteEventArgs e)
        {
            if (BeforeDelete != null)
            {
                BeforeDelete(this, e);
            }
        }

        new public static event DeleteEventHandler AfterDelete;
        new protected virtual void FireAfterDelete(DeleteEventArgs e)
        {
            if (AfterDelete != null)
            {
                AfterDelete(this, e);
            }
        }
    }

    /// <summary>
    /// ONLY FOR INTERNAL USE.
    /// This is needed due to a design flaw where the Umbraco membership provider is located 
    /// in a separate project referencing this project, which means we can't call special methods
    /// directly on the UmbracoMemberShipMember class.
    /// This is a helper implementation only to be able to use the encryption functionality 
    /// of the membership provides (which are protected).
    /// </summary>
    public class MemberShipHelper : MembershipProvider
    {
        public override string ApplicationName
        {
            get
            {
                throw new NotImplementedException();
            }
            set
            {
                throw new NotImplementedException();
            }
        }

        public override bool ChangePassword(string username, string oldPassword, string newPassword)
        {
            throw new NotImplementedException();
        }

        public override bool ChangePasswordQuestionAndAnswer(string username, string password, string newPasswordQuestion, string newPasswordAnswer)
        {
            throw new NotImplementedException();
        }

        public override MembershipUser CreateUser(string username, string password, string email, string passwordQuestion, string passwordAnswer, bool isApproved, object providerUserKey, out MembershipCreateStatus status)
        {
            throw new NotImplementedException();
        }

        public override bool DeleteUser(string username, bool deleteAllRelatedData)
        {
            throw new NotImplementedException();
        }

        public string EncodePassword(string password, MembershipPasswordFormat pwFormat)
        {
            string encodedPassword = password;
            switch (pwFormat)
            {
                case MembershipPasswordFormat.Clear:
                    break;
                case MembershipPasswordFormat.Encrypted:
                    encodedPassword =
                      Convert.ToBase64String(EncryptPassword(Encoding.Unicode.GetBytes(password)));
                    break;
                case MembershipPasswordFormat.Hashed:
                    HMACSHA1 hash = new HMACSHA1();
                    hash.Key = Encoding.Unicode.GetBytes(password);
                    encodedPassword =
                      Convert.ToBase64String(hash.ComputeHash(Encoding.Unicode.GetBytes(password)));
                    break;
            }
            return encodedPassword;
        }

        public override bool EnablePasswordReset
        {
            get { throw new NotImplementedException(); }
        }

        public override bool EnablePasswordRetrieval
        {
            get { throw new NotImplementedException(); }
        }

        public override MembershipUserCollection FindUsersByEmail(string emailToMatch, int pageIndex, int pageSize, out int totalRecords)
        {
            throw new NotImplementedException();
        }

        public override MembershipUserCollection FindUsersByName(string usernameToMatch, int pageIndex, int pageSize, out int totalRecords)
        {
            throw new NotImplementedException();
        }

        public override MembershipUserCollection GetAllUsers(int pageIndex, int pageSize, out int totalRecords)
        {
            throw new NotImplementedException();
        }

        public override int GetNumberOfUsersOnline()
        {
            throw new NotImplementedException();
        }

        public override string GetPassword(string username, string answer)
        {
            throw new NotImplementedException();
        }

        public override MembershipUser GetUser(string username, bool userIsOnline)
        {
            throw new NotImplementedException();
        }

        public override MembershipUser GetUser(object providerUserKey, bool userIsOnline)
        {
            throw new NotImplementedException();
        }

        public override string GetUserNameByEmail(string email)
        {
            throw new NotImplementedException();
        }

        public override int MaxInvalidPasswordAttempts
        {
            get { throw new NotImplementedException(); }
        }

        public override int MinRequiredNonAlphanumericCharacters
        {
            get { throw new NotImplementedException(); }
        }

        public override int MinRequiredPasswordLength
        {
            get { throw new NotImplementedException(); }
        }

        public override int PasswordAttemptWindow
        {
            get { throw new NotImplementedException(); }
        }

        public override MembershipPasswordFormat PasswordFormat
        {
            get { throw new NotImplementedException(); }
        }

        public override string PasswordStrengthRegularExpression
        {
            get { throw new NotImplementedException(); }
        }

        public override bool RequiresQuestionAndAnswer
        {
            get { throw new NotImplementedException(); }
        }

        public override bool RequiresUniqueEmail
        {
            get { throw new NotImplementedException(); }
        }

        public override string ResetPassword(string username, string answer)
        {
            throw new NotImplementedException();
        }

        public override bool UnlockUser(string userName)
        {
            throw new NotImplementedException();
        }

        public override void UpdateUser(MembershipUser user)
        {
            throw new NotImplementedException();
        }

        public override bool ValidateUser(string username, string password)
        {
            throw new NotImplementedException();
        }
    }
}