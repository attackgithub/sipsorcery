//-----------------------------------------------------------------------------
// Filename: SIPUserField.cs
//
// Description: 
// Encapsulates the format for the SIP Contact, From and To headers
//
// History:
// 21 Apr 2006	Aaron Clauson	Created.
// 04 Sep 2008  Aaron Clauson   Changed display name to always use quotes. Some SIP stacks were
//                              found to have porblems with a comma in a non-quoted display name.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2006 Aaron Clauson (aaronc@blueface.ie), Blue Face Ltd, Dublin, Ireland (www.blueface.ie)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of Blue Face Ltd. 
// nor the names of its contributors may be used to endorse or promote products derived from this software without specific 
// prior written permission. 
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, 
// BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. 
// IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, 
// OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, 
// OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, 
// OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE 
// POSSIBILITY OF SUCH DAMAGE.
//-----------------------------------------------------------------------------

using System;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using SIPSorcery.Sys;
using log4net;

#if UNITTEST
using NUnit.Framework;
#endif

namespace SIPSorcery.SIP
{
	/// <summary>
	/// name-addr      =  [ display-name ] LAQUOT addr-spec RAQUOT
	/// addr-spec      =  SIP-URI / SIPS-URI / absoluteURI
	/// SIP-URI          =  "sip:" [ userinfo ] hostport
	/// uri-parameters [ headers ]
	/// SIPS-URI         =  "sips:" [ userinfo ] hostport
	/// uri-parameters [ headers ]
	/// userinfo         =  ( user / telephone-subscriber ) [ ":" password ] "@"
	///
	/// If no "<" and ">" are present, all parameters after the URI are header
	/// parameters, not URI parameters.
	/// </summary>
    [DataContract]
	public class SIPUserField
	{
        private const char PARAM_TAG_DELIMITER = ';';

        private static ILog logger = AssemblyState.logger;

        [DataMember]
		public string Name;
		
        [DataMember]
        public SIPURI URI;

        [DataMember]
        public SIPParameters Parameters = new SIPParameters(null, PARAM_TAG_DELIMITER);

		public SIPUserField()
		{}
	
		public SIPUserField(string name, SIPURI uri, string paramsAndHeaders)
		{
			Name = name;
			URI = uri;

            Parameters = new SIPParameters(paramsAndHeaders, PARAM_TAG_DELIMITER);
		}

        public static SIPUserField ParseSIPUserField(string userFieldStr)
        {
            if (userFieldStr.IsNullOrBlank())
            {
                throw new ArgumentException("A SIPUserField cannot be parsed from an empty string.");
            }

            SIPUserField userField = new SIPUserField();
            string trimUserField = userFieldStr.Trim();

            int position = trimUserField.IndexOf('<');

            if (position == -1)
            {
                // Treat the field as a URI only, except that all parameters are Header parameters and not URI parameters 
                // (RFC3261 section 20.39 which refers to 20.10 for parsing rules).
                string uriStr = trimUserField;
                int paramDelimPosn = trimUserField.IndexOf(PARAM_TAG_DELIMITER);

                if (paramDelimPosn != -1)
                {
                    string paramStr = trimUserField.Substring(paramDelimPosn + 1).Trim();
                    userField.Parameters = new SIPParameters(paramStr, PARAM_TAG_DELIMITER);
                    uriStr = trimUserField.Substring(0, paramDelimPosn);
                }

                userField.URI = SIPURI.ParseSIPURI(uriStr);
            }
            else
            {
                if (position > 0)
                {
                    userField.Name = trimUserField.Substring(0, position).Trim().Trim('"');
                    trimUserField = trimUserField.Substring(position, trimUserField.Length - position);
                }

                int addrSpecLen = trimUserField.Length;
                position = trimUserField.IndexOf('>');
                if (position != -1)
                {
                    addrSpecLen = trimUserField.Length - 1;
                    if (position != -1)
                    {
                        addrSpecLen = position - 1;

                        string paramStr = trimUserField.Substring(position + 1).Trim();
                        userField.Parameters = new SIPParameters(paramStr, PARAM_TAG_DELIMITER);
                    }

                    string addrSpec = trimUserField.Substring(1, addrSpecLen);

                    userField.URI = SIPURI.ParseSIPURI(addrSpec);
                }
                else
                {
                    logger.Warn("A SIPUserField was missing the right quote, " + userFieldStr + ".");
                }
            }

            return userField;
        }

		public override string ToString()
		{
			try
			{
				string userFieldStr = null;
			
				if(Name != null)
				{
					/*if(Regex.Match(Name, @"\s").Success)
					{
						userFieldStr = "\"" + Name + "\" ";
					}
					else
					{
						userFieldStr = Name + " ";
					}*/

                    userFieldStr = "\"" + Name + "\" ";
				}
			
				userFieldStr += "<" + URI.ToString() + ">" + Parameters.ToString();

				return userFieldStr;
			}
			catch(Exception excp)
			{
				logger.Debug("Exception SIPUserField ToString. " + excp.Message);
				return null;
			}
		}

        public SIPUserField CopyOf()
        {
            SIPUserField copy = new SIPUserField();
            copy.Name = Name;
            copy.URI = URI.CopyOf();
            copy.Parameters = Parameters.CopyOf();

            return copy;
        }
		
		#region Unit testing.

		#if UNITTEST
	
		[TestFixture]
		public class SIPUserFieldUnitTest
		{
			[TestFixtureSetUp]
			public void Init()
			{}
        
            [TestFixtureTearDown]
			public void Dispose()
			{}
			
			[Test]
			public void SampleTest()
			{
				Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);
				
				Assert.IsTrue(true, "True was false.");
			}

            [Test]
            public void ParamsInUserPortionURITest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                SIPUserField userField = SIPUserField.ParseSIPUserField("<sip:C=on;t=DLPAN@10.0.0.1:5060;lr>");

                Assert.IsTrue("C=on;t=DLPAN" == userField.URI.User, "SIP user portion parsed incorrectly.");
                Assert.IsTrue("10.0.0.1:5060" == userField.URI.Host, "SIP host portion parsed incorrectly.");

                Console.WriteLine("-----------------------------------------");
            }
		}

		#endif

		#endregion
	}
}
