import PropTypes from 'prop-types';
import React, { useState } from 'react';
import Alert from 'Components/Alert';
import TextInput from 'Components/Form/TextInput';
import Icon from 'Components/Icon';
import Button from 'Components/Link/Button';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import { icons, kinds, sortDirections } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import InteractiveSearchRow from './InteractiveSearchRow';
import styles from './InteractiveSearch.css';

const columns = [
  {
    name: 'protocol',
    label: 'Source',
    isSortable: true,
    isVisible: true
  },
  {
    name: 'age',
    label: 'Age',
    isSortable: true,
    isVisible: true
  },
  {
    name: 'title',
    label: 'Title',
    isSortable: true,
    isVisible: true
  },
  {
    name: 'indexer',
    label: 'Indexer',
    isSortable: true,
    isVisible: true
  },
  {
    name: 'size',
    label: 'Size',
    isSortable: true,
    isVisible: true
  },
  {
    name: 'peers',
    label: 'Peers',
    isSortable: true,
    isVisible: true
  },
  {
    name: 'qualityWeight',
    label: 'Quality',
    isSortable: true,
    isVisible: true
  },
  {
    name: 'customFormatScore',
    label: React.createElement(Icon, {
      name: icons.SCORE,
      title: () => translate('CustomFormatScore')
    }),
    isSortable: true,
    isVisible: true
  },
  {
    name: 'indexerFlags',
    label: React.createElement(Icon, {
      name: icons.FLAG,
      title: () => translate('IndexerFlags')
    }),
    isSortable: true,
    isVisible: true
  },
  {
    name: 'rejections',
    label: React.createElement(Icon, {
      name: icons.DANGER,
      title: 'Rejections'
    }),
    isSortable: true,
    fixedSortDirection: sortDirections.ASCENDING,
    isVisible: true
  },
  {
    name: 'releaseWeight',
    label: React.createElement(Icon, { name: icons.DOWNLOAD }),
    isSortable: true,
    fixedSortDirection: sortDirections.ASCENDING,
    isVisible: true
  }
];

function InteractiveSearch(props) {
  const {
    type,
    searchPayload,
    isFetching,
    isPopulated,
    error,
    totalReleasesCount,
    items,
    sortKey,
    sortDirection,
    longDateFormat,
    timeFormat,
    onSortPress,
    onGrabPress,
    dispatchFetchReleases
  } = props;

  const [customTerm, setCustomTerm] = useState('');

  const onCustomTermChange = ({ value }) => {
    setCustomTerm(value);
  };

  const onCustomSearchSubmit = (event) => {
    event.preventDefault();

    if (customTerm.trim()) {
      dispatchFetchReleases({
        ...searchPayload,
        term: customTerm
      });
    }
  };

  return (
    <div>
      {
        // Interactive Search's default query is built from the book's own title/author
        // metadata and can't be edited. When indexer release naming doesn't line up
        // with that (or the match is simply wrong), this box lets you override the
        // search term the indexers are actually queried with.
        type === 'book' ?
          <form
            className={styles.customSearch}
            onSubmit={onCustomSearchSubmit}
          >
            <TextInput
              className={styles.customSearchInput}
              placeholder={translate('SearchForCustomTerm')}
              name="customTerm"
              value={customTerm}
              onChange={onCustomTermChange}
            />

            <Button
              type="submit"
              isDisabled={!customTerm.trim()}
            >
              {translate('Search')}
            </Button>
          </form> :
          null
      }

      {
        isFetching ? <LoadingIndicator /> : null
      }

      {
        !isFetching && error ?
          <div className={styles.blankpad}>
            Unable to load results for this book search. Try again later
          </div> :
          null
      }

      {
        !isFetching && isPopulated && !totalReleasesCount ?
          <Alert kind={kinds.INFO}>
            {translate('NoResultsFound')}
          </Alert> :
          null
      }

      {
        !!totalReleasesCount && isPopulated && !items.length ?
          <Alert kind={kinds.WARNING}>
            {translate('AllResultsAreHiddenByTheAppliedFilter')}
          </Alert> :
          null
      }

      {
        isPopulated && !!items.length ?
          <Table
            columns={columns}
            sortKey={sortKey}
            sortDirection={sortDirection}
            onSortPress={onSortPress}
          >
            <TableBody>
              {
                items.map((item) => {
                  return (
                    <InteractiveSearchRow
                      key={`${item.indexerId}-${item.guid}`}
                      {...item}
                      searchPayload={searchPayload}
                      longDateFormat={longDateFormat}
                      timeFormat={timeFormat}
                      onGrabPress={onGrabPress}
                    />
                  );
                })
              }
            </TableBody>
          </Table> :
          null
      }

      {
        totalReleasesCount !== items.length && !!items.length ?
          <div className={styles.filteredMessage}>
            {translate('SomeResultsAreHiddenByTheAppliedFilter')}
          </div> :
          null
      }
    </div>
  );
}

InteractiveSearch.propTypes = {
  searchPayload: PropTypes.object.isRequired,
  isFetching: PropTypes.bool.isRequired,
  isPopulated: PropTypes.bool.isRequired,
  error: PropTypes.object,
  totalReleasesCount: PropTypes.number.isRequired,
  items: PropTypes.arrayOf(PropTypes.object).isRequired,
  sortKey: PropTypes.string,
  sortDirection: PropTypes.string,
  type: PropTypes.string.isRequired,
  longDateFormat: PropTypes.string.isRequired,
  timeFormat: PropTypes.string.isRequired,
  onSortPress: PropTypes.func.isRequired,
  onGrabPress: PropTypes.func.isRequired,
  dispatchFetchReleases: PropTypes.func.isRequired
};

export default InteractiveSearch;
