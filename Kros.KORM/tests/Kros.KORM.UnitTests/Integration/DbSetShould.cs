using FluentAssertions;
using Kros.Data.SqlServer;
using Kros.KORM.Converter;
using Kros.KORM.Metadata;
using Kros.KORM.Metadata.Attribute;
using Kros.KORM.Query;
using Kros.KORM.UnitTests.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Kros.KORM.UnitTests.Integration
{
    public class DbSetShould : DatabaseTestBase
    {

        #region SQL Scripts

        private const string Table_TestTable = "People";

        private static string CreateTable_TestTable =
            $@"CREATE TABLE[dbo].[{Table_TestTable}] (
    [Id] [int] NOT NULL,
    [Age] [int] NULL,
    [FirstName] [nvarchar] (50) NULL,
    [LastName] [nvarchar] (50) NULL,
    [Address] [nvarchar] (50) NULL
) ON[PRIMARY];
";

        #endregion

        [Fact]
        public void InsertData()
        {
            using (var korm = CreateDatabase(CreateTable_TestTable))
            {
                var dbSet = korm.Query<Person>().AsDbSet();

                dbSet.Add(new Person()
                {
                    Id = 1,
                    FirstName = "Milan",
                    LastName = "Martiniak",
                    Age = 32,
                    Address = new List<string>() { "Petzvalova", "Pekna", "Zelena" }
                });

                dbSet.Add(new Person()
                {
                    Id = 2,
                    FirstName = "Peter",
                    LastName = "Juráček",
                    Age = 14,
                    Address = new List<string>() { "Novozámocká" }
                });

                dbSet.CommitChanges();

                var person = korm.Query<Person>().FirstOrDefault(p => p.Id == 1);

                person.Id.Should().Be(1);
                person.Age.Should().Be(32);
                person.FirstName.Should().Be("Milan");
                person.LastName.Should().Be("Martiniak");
                person.Address.ShouldBeEquivalentTo(new List<string>() { "Petzvalova", "Pekna", "Zelena" });
            }
        }

        [Fact]
        public void GeneratePrimaryKey()
        {
            OnGeneratePrimaryKey(dbSet => dbSet.CommitChanges());
        }

        [Fact]
        public void GeneratePrimaryKeyWhenBulkInsertIsCall()
        {
            OnGeneratePrimaryKey(dbSet => dbSet.BulkInsert());
        }

        private void OnGeneratePrimaryKey(Action<IDbSet<Person>> commitAction)
        {
            using (var korm = CreateDatabase(CreateTable_TestTable,
                            SqlServerIdGenerator.GetIdStoreTableCreationScript(),
                            SqlServerIdGenerator.GetStoredProcedureCreationScript()))
            {
                var dbSet = korm.Query<Person>().AsDbSet();

                var sourcePeople = new List<Person>() {
                    new Person() { FirstName = "Milan" },
                    new Person() { FirstName = "Peter" },
                    new Person() { FirstName = "Milada" }
                };

                dbSet.Add(sourcePeople);

                commitAction(dbSet);

                var id = 1;
                foreach (var item in sourcePeople)
                {
                    item.Id.Should().Be(id++);
                }

                var people = korm.Query<Person>().OrderBy(p => p.Id);

                var sourceEnumerator = sourcePeople.GetEnumerator();
                id = 1;
                foreach (var item in people)
                {
                    sourceEnumerator.MoveNext();
                    var source = sourceEnumerator.Current;

                    item.Id.Should().Be(id++);
                    item.FirstName.Should().Be(source.FirstName);
                }
            }
        }

        [Fact]
        public void DoNotGeneratePrimaryKeyIfFilled()
        {
            using (var korm = CreateDatabase(CreateTable_TestTable,
               SqlServerIdGenerator.GetIdStoreTableCreationScript(),
               SqlServerIdGenerator.GetStoredProcedureCreationScript()))
            {
                var dbSet = korm.Query<Person>().AsDbSet();

                var sourcePeople = new List<Person>() {
                    new Person() { Id = 5,  FirstName = "Milan" },
                    new Person() { Id = 7, FirstName = "Peter" },
                    new Person() { Id = 9, FirstName = "Milada" }
                };

                dbSet.Add(sourcePeople);

                dbSet.CommitChanges();

                var id = 5;
                foreach (var item in sourcePeople)
                {
                    item.Id.Should().Be(id);
                    id += 2;
                }

                var people = korm.Query<Person>().OrderBy(p => p.Id);

                var sourceEnumerator = sourcePeople.GetEnumerator();
                id = 5;
                foreach (var item in people)
                {
                    sourceEnumerator.MoveNext();
                    var source = sourceEnumerator.Current;

                    item.Id.Should().Be(id);
                    item.FirstName.Should().Be(source.FirstName);
                    id += 2;
                }
            }
        }

        [Fact]
        public void DoNotGeneratePrimaryKeyIfKeyIsNotAutoIncrement()
        {
            using (var korm = CreateDatabase(CreateTable_TestTable,
               SqlServerIdGenerator.GetIdStoreTableCreationScript(),
               SqlServerIdGenerator.GetStoredProcedureCreationScript()))
            {
                var dbSet = korm.Query<Foo>().AsDbSet();

                var sourcePeople = new List<Foo>() {
                    new Foo(),
                    new Foo(),
                    new Foo(),
                };

                dbSet.Add(sourcePeople);

                dbSet.CommitChanges();

                sourcePeople.Select(p => p.Id).ShouldBeEquivalentTo(new int[] { 0, 0, 0 });

                var people = korm.Query<Person>().AsEnumerable();
                people.Select(p => p.Id).ShouldBeEquivalentTo(new int[] { 0, 0, 0 });
            }
        }

        [Fact]
        public void IteratedThroughItemsOnlyOnceWhenGeneratePrimaryKeys()
        {
            using (var korm = CreateDatabase(CreateTable_TestTable,
               SqlServerIdGenerator.GetIdStoreTableCreationScript(),
               SqlServerIdGenerator.GetStoredProcedureCreationScript()))
            {
                var dbSet = korm.Query<Person>().AsDbSet();

                var iterationCount = 0;
                IEnumerable<Person> SourceItems()
                {
                    iterationCount++;
                    yield return new Person() { Id = 5, FirstName = "Milan" };
                }
                var sourcePeople = SourceItems();

                dbSet.BulkInsert(sourcePeople);

                iterationCount.Should().Be(1);
            }
        }

        [Alias("People")]
        private class Person
        {
            [Key(AutoIncrementMethodType.Custom)]
            public int Id { get; set; }

            public int Age { get; set; }

            public string FirstName { get; set; }

            public string LastName { get; set; }

            [Converter(typeof(AddressConverter))]
            public List<string> Address { get; set; }
        }

        [Alias("People")]
        private class Foo
        {
            [Key(AutoIncrementMethodType.None)]
            public int Id { get; set; }
        }

        private class AddressConverter : IConverter
        {
            public object Convert(object value) =>
                value != null ? value.ToString().Split('#').ToList() : new List<string>();

            public object ConvertBack(object value) =>
                value is List<string> address && address.Count > 0 ? string.Join("#", address) : null;
        }
    }
}