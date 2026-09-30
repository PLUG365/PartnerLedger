using System;
using Microsoft.Xrm.Sdk;
using PartnerLedger.Plugins;
using Xunit;

namespace PartnerLedger.Plugins.Tests
{
    public sealed class ContactStandardUpdateGuardTests
    {
        [Fact]
        public void 氏名と状態と連絡先のUpdateは許可する()
        {
            var target = new Entity("pl_contact", Guid.NewGuid())
            {
                ["pl_name"] = "更新後の氏名",
                ["pl_statuscode"] = new OptionSetValue(100000001),
                ["pl_departmentrole"] = "営業部",
                ["pl_email"] = "updated@example.com",
                ["pl_phone"] = "03-0000-0000",
            };

            ContactStandardUpdateGuardPlugin.Validate(target);
        }

        [Fact]
        public void 文字列のUpdateは正規化し任意項目の空文字をクリアする()
        {
            var target = new Entity("pl_contact", Guid.NewGuid())
            {
                ["pl_name"] = "  更新後の氏名  ",
                ["pl_departmentrole"] = "   ",
            };

            ContactStandardUpdateGuardPlugin.Validate(target);

            Assert.Equal("更新後の氏名", target.GetAttributeValue<string>("pl_name"));
            Assert.Null(target["pl_departmentrole"]);
        }

        [Fact]
        public void Dataverse標準Updateが付与する主キー属性は受け入れる()
        {
            var contactId = Guid.NewGuid();
            var target = new Entity("pl_contact", contactId)
            {
                ["pl_contactid"] = contactId,
                ["pl_name"] = "更新後の氏名",
            };

            ContactStandardUpdateGuardPlugin.Validate(target);
        }

        [Fact]
        public void 退職は在籍状態とDataverse非アクティブ状態の組合せだけ許可する()
        {
            var target = new Entity("pl_contact", Guid.NewGuid())
            {
                ["pl_name"] = "退職予定者",
                ["pl_statuscode"] = new OptionSetValue(100000002),
                ["statecode"] = new OptionSetValue(1),
                ["statuscode"] = new OptionSetValue(2),
            };

            ContactStandardUpdateGuardPlugin.Validate(target);
        }

        [Fact]
        public void 退職済み行を在籍へ戻すUpdateは拒否する()
        {
            var target = new Entity("pl_contact", Guid.NewGuid())
            {
                ["pl_statuscode"] = new OptionSetValue(100000000),
            };
            var existing = new Entity("pl_contact", target.Id)
            {
                ["pl_statuscode"] = new OptionSetValue(100000002),
                ["statecode"] = new OptionSetValue(1),
                ["statuscode"] = new OptionSetValue(2),
            };

            var exception = Assert.Throws<InvalidPluginExecutionException>(
                () => ContactStandardUpdateGuardPlugin.Validate(target, existing));

            Assert.Contains("再有効化", exception.Message);
        }

        [Theory]
        [InlineData(100000000, 0, 1)]
        [InlineData(100000001, 1, 2)]
        [InlineData(100000002, 0, 1)]
        [InlineData(100000002, 1, 1)]
        public void 退職以外または不一致の状態組合せは拒否する(int contactStatus, int state, int status)
        {
            var target = new Entity("pl_contact", Guid.NewGuid())
            {
                ["pl_statuscode"] = new OptionSetValue(contactStatus),
                ["statecode"] = new OptionSetValue(state),
                ["statuscode"] = new OptionSetValue(status),
            };

            Assert.Throws<InvalidPluginExecutionException>(() => ContactStandardUpdateGuardPlugin.Validate(target));
        }

        [Fact]
        public void 退職状態だけのUpdateはアーカイブ漏れとして拒否する()
        {
            var target = new Entity("pl_contact", Guid.NewGuid())
            {
                ["pl_statuscode"] = new OptionSetValue(100000002),
            };

            Assert.Throws<InvalidPluginExecutionException>(() => ContactStandardUpdateGuardPlugin.Validate(target));
        }

        [Fact]
        public void 主キー属性の型が不正なら拒否する()
        {
            var target = new Entity("pl_contact", Guid.NewGuid())
            {
                ["pl_contactid"] = "changed",
            };

            var exception = Assert.Throws<InvalidPluginExecutionException>(
                () => ContactStandardUpdateGuardPlugin.Validate(target));

            Assert.Contains("pl_contactid", exception.Message);
        }

        [Theory]
        [InlineData("pl_partnerlookup")]
        [InlineData("pl_registeredat")]
        [InlineData("pl_registrationkey")]
        [InlineData("createdby")]
        [InlineData("ownerid")]
        public void 所属と監査と権威列のUpdateは拒否する(string attributeName)
        {
            var target = new Entity("pl_contact", Guid.NewGuid())
            {
                [attributeName] = attributeName == "pl_partnerlookup"
                    ? (object)new EntityReference("pl_partner", Guid.NewGuid())
                    : "changed",
            };

            var exception = Assert.Throws<InvalidPluginExecutionException>(() => ContactStandardUpdateGuardPlugin.Validate(target));

            Assert.Contains(attributeName, exception.Message);
        }

        [Fact]
        public void 未知列はUpdateでも拒否する()
        {
            var target = new Entity("pl_contact", Guid.NewGuid())
            {
                ["pl_unknownclientfield"] = "changed",
            };

            Assert.Throws<InvalidPluginExecutionException>(() => ContactStandardUpdateGuardPlugin.Validate(target));
        }
    }
}
